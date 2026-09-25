using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace ThrottledLogging;

/// <summary>
/// Provides rate-limited logging by suppressing duplicate log entries within a configurable time interval.
/// Tracks log keys and their last-logged timestamps, automatically cleaning up expired entries via a background timer.
/// </summary>
public class ThrottledLogger
{
    /// <summary>
    /// Represents a tracked log entry with its last log timestamp, the number of suppressed occurrences,
    /// the timestamp of the most recent call, and the longest throttle interval used since the last log.
    /// </summary>
    private struct Entry : IEquatable<Entry>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Entry"/> struct with the specified field values.
        /// </summary>
        /// <param name="lastLogTick">The <see cref="Stopwatch"/> timestamp when the key was last logged.</param>
        /// <param name="suppressedCount">The number of log calls suppressed since the last successful log.</param>
        /// <param name="lastSeenTick">The <see cref="Stopwatch"/> timestamp of the most recent call for the key, whether logged or suppressed.</param>
        /// <param name="interval">The longest throttle interval passed for the key since it was last logged.</param>
        public Entry(long lastLogTick, int suppressedCount, long lastSeenTick, TimeSpan interval)
        {
            LastLogTick = lastLogTick;
            SuppressedCount = suppressedCount;
            LastSeenTick = lastSeenTick;
            Interval = interval;
        }

        /// <summary>
        /// The <see cref="Stopwatch"/> timestamp when the key was last logged.
        /// </summary>
        public long LastLogTick
        {
            get;
        }

        /// <summary>
        /// The number of log calls suppressed since the last successful log.
        /// </summary>
        public int SuppressedCount
        {
            get;
        }

        /// <summary>
        /// The <see cref="Stopwatch"/> timestamp of the most recent call for the key, whether logged or suppressed.
        /// Used by cleanup to measure how long the key has been idle.
        /// </summary>
        public long LastSeenTick
        {
            get;
        }

        /// <summary>
        /// The longest throttle interval passed for the key since it was last logged.
        /// Used by cleanup to avoid removing an entry whose throttle window is still open.
        /// </summary>
        public TimeSpan Interval
        {
            get;
        }

        /// <summary>
        /// Determines whether this instance and another <see cref="Entry"/> have the same field values.
        /// </summary>
        /// <param name="other">The other <see cref="Entry"/> to compare against.</param>
        /// <returns><see langword="true"/> if all fields of both instances are equal; otherwise <see langword="false"/>.</returns>
        /// <remarks>
        /// Implementing <see cref="IEquatable{T}"/> lets <see cref="ConcurrentDictionary{TKey, TValue}.TryUpdate(TKey, TValue, TValue)"/>
        /// use the non-boxing generic comparer instead of falling back to a boxing <see cref="object.Equals(object?)"/> comparison.
        /// </remarks>
        public bool Equals(Entry other)
            => LastLogTick == other.LastLogTick
               && SuppressedCount == other.SuppressedCount
               && LastSeenTick == other.LastSeenTick
               && Interval == other.Interval;

        /// <summary>
        /// Determines whether this instance and a specified object, which must also be an <see cref="Entry"/>, have the same field values.
        /// </summary>
        /// <param name="obj">The object to compare with the current instance.</param>
        /// <returns><see langword="true"/> if <paramref name="obj"/> is an <see cref="Entry"/> equal to this instance; otherwise <see langword="false"/>.</returns>
        public override bool Equals(object? obj) => obj is Entry other && Equals(other);

        /// <summary>
        /// Returns a hash code based on all fields of the entry.
        /// </summary>
        /// <returns>A hash code for the current instance.</returns>
        public override int GetHashCode() => HashCode.Combine(LastLogTick, SuppressedCount, LastSeenTick, Interval);
    }

    /// <summary>
    /// The longest cleanup period accepted by <see cref="Configure(TimeSpan, TimeSpan)"/>, which is the longest period
    /// supported by <see cref="Timer"/> (4294967294 milliseconds, about 49.7 days).
    /// </summary>
    private static readonly TimeSpan MaxCleanupPeriod = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    /// <summary>
    /// The shortest cleanup period accepted by <see cref="Configure(TimeSpan, TimeSpan)"/>. <see cref="Timer"/> truncates
    /// periods to whole milliseconds, and a period that truncates to zero would run cleanup only once.
    /// </summary>
    private static readonly TimeSpan MinCleanupPeriod = TimeSpan.FromMilliseconds(1);

    /// <summary>
    /// Serializes <see cref="Configure(TimeSpan, TimeSpan)"/> calls, so the expiry and cleanup period are applied together.
    /// </summary>
    private static readonly Lock ConfigureLock = new();

    /// <summary>
    /// Represents the timer used to schedule periodic cleanup operations.
    /// </summary>
    private static readonly Timer CleanupTimer;

    /// <summary>
    /// A thread-safe mapping of <see cref="ILogger"/> instances to their corresponding <see cref="ThrottledLogger"/> instances,
    /// </summary>
    private static readonly ConditionalWeakTable<ILogger, ThrottledLogger> Instances = new();

    /// <summary>
    /// The age threshold, in <see cref="TimeSpan"/> ticks, after which a log entry is considered expired and eligible for cleanup.
    /// Stored as a <see cref="long"/> and accessed through <see cref="Volatile"/>, so the cleanup thread never reads a torn value.
    /// </summary>
    private static long _expiryTicks;

    /// <summary>
    /// A thread-safe dictionary that tracks log keys and their associated log entry data (last log timestamp and suppressed count) for this throttler instance.
    /// </summary>
    private readonly ConcurrentDictionary<string, Entry> _tracker = new();

    /// <summary>
    /// Initializes static members of the <see cref="ThrottledLogger"/> class, setting up the default cleanup period and starting the background timer for cleanup of expired entries.
    /// </summary>
    static ThrottledLogger()
    {
        var defaultExpiry = TimeSpan.FromHours(1);
        var defaultCleanupPeriod = TimeSpan.FromHours(1);

        _expiryTicks = defaultExpiry.Ticks;
        CleanupTimer = CreateCleanupTimer(OnCleanupTimer, defaultCleanupPeriod);
    }

    /// <summary>
    /// Configures the global expiry threshold and cleanup timer period for all <see cref="ThrottledLogger"/> instances.
    /// </summary>
    /// <param name="expiry">
    /// How long an entry must be idle (no logged or suppressed calls) before it is eligible for cleanup.
    /// Entries whose throttle interval has not yet elapsed are never removed, regardless of this value,
    /// so an entry is retained for at least its throttle interval. If one key is used with different intervals, the
    /// retained interval is the one passed to the most recently logged call (or longer, if a later suppressed call
    /// passed a longer one), so an expiry shorter than the longest interval may let that longer interval end early.
    /// Avoid combining very long intervals
    /// (such as <see cref="TimeSpan.MaxValue"/>) with an unbounded set of keys, as memory then grows with the key count.
    /// An entry removed by cleanup discards any suppressed count not yet reported, so the next message for that key
    /// is logged without the suppressed-count suffix.
    /// </param>
    /// <param name="cleanupPeriod">
    /// How often the background cleanup timer runs, from 1 millisecond up to 4294967294 milliseconds (about 49.7 days).
    /// Pass <see cref="Timeout.InfiniteTimeSpan"/> to disable cleanup.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="expiry"/> is negative, or <paramref name="cleanupPeriod"/> is less than 1 millisecond (other than
    /// <see cref="Timeout.InfiniteTimeSpan"/>) or greater than 4294967294 milliseconds.
    /// </exception>
    public static void Configure(TimeSpan expiry, TimeSpan cleanupPeriod)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(expiry, TimeSpan.Zero);

        if (cleanupPeriod != Timeout.InfiniteTimeSpan)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(cleanupPeriod, MinCleanupPeriod);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(cleanupPeriod, MaxCleanupPeriod);
        }

        // Applied together so concurrent calls cannot pair one call's expiry with another's period. The expiry is written
        // before the timer is changed; the timer's internal locking publishes it to the callback thread.
        lock (ConfigureLock)
        {
            Volatile.Write(ref _expiryTicks, expiry.Ticks);
            CleanupTimer.Change(cleanupPeriod, cleanupPeriod);
        }
    }

    /// <summary>
    /// Determines whether a log entry identified by <paramref name="key"/> should be emitted,
    /// based on the specified throttle <paramref name="interval"/>.
    /// </summary>
    /// <param name="key">A unique identifier for the log message to throttle (e.g., a message template or category).</param>
    /// <param name="interval">The minimum <see cref="TimeSpan"/> that must elapse before the same key is logged again.</param>
    /// <param name="suppressedCount">
    /// When the method returns <see langword="true"/>, contains the number of log calls that were
    /// suppressed since the previous successful log for this key; otherwise, <c>0</c>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the log entry should be emitted; <see langword="false"/> if it is
    /// suppressed because the throttle interval has not yet elapsed.
    /// </returns>
    public bool ShouldLog(string key, TimeSpan interval, out int suppressedCount)
    {
        var tick = Stopwatch.GetTimestamp();

        while (true)
        {
            if (_tracker.TryGetValue(key, out var existing))
            {
                // A non-positive interval always logs. Checked explicitly because the elapsed time is negative when a
                // concurrent call with a later timestamp has already updated the entry.
                if (interval > TimeSpan.Zero && Stopwatch.GetElapsedTime(existing.LastLogTick, tick) < interval)
                {
                    var suppressedEntry = new Entry(
                        existing.LastLogTick,
                        IncrementSaturating(existing.SuppressedCount),
                        Math.Max(existing.LastSeenTick, tick),
                        existing.Interval > interval ? existing.Interval : interval);

                    if (_tracker.TryUpdate(key, suppressedEntry, existing))
                    {
                        suppressedCount = 0;
                        return false;
                    }

                    continue;
                }

                if (_tracker.TryUpdate(key, new Entry(Math.Max(existing.LastLogTick, tick), 0, Math.Max(existing.LastSeenTick, tick), interval), existing))
                {
                    suppressedCount = existing.SuppressedCount;
                    return true;
                }

                continue;
            }

            if (_tracker.TryAdd(key, new Entry(tick, 0, tick, interval)))
            {
                suppressedCount = 0;
                return true;
            }
        }
    }

    /// <summary>
    /// Removes any tracked throttle state for <paramref name="key"/>, so the next call for that key is treated as the first.
    /// </summary>
    /// <param name="key">The throttling key to reset.</param>
    public void Reset(string key) => _tracker.TryRemove(key, out _);

    /// <summary>
    /// Attempts to get the number of log calls currently suppressed for <paramref name="key"/>.
    /// </summary>
    /// <param name="key">The throttling key to query.</param>
    /// <param name="suppressedCount">The number of suppressed calls recorded for the key, if tracked.</param>
    /// <returns><see langword="true"/> if the key is currently tracked; otherwise <see langword="false"/>.</returns>
    public bool TryGetSuppressedCount(string key, out int suppressedCount)
    {
        if (_tracker.TryGetValue(key, out var entry))
        {
            suppressedCount = entry.SuppressedCount;
            return true;
        }

        suppressedCount = 0;
        return false;
    }

    /// <summary>
    /// Increments a suppressed count by one, saturating at <see cref="int.MaxValue"/> instead of overflowing.
    /// </summary>
    /// <param name="count">The current suppressed count.</param>
    /// <returns><paramref name="count"/> + 1, or <see cref="int.MaxValue"/> if <paramref name="count"/> is already at the maximum.</returns>
    internal static int IncrementSaturating(int count)
        => count == int.MaxValue ? count : count + 1;

    /// <summary>
    /// Creates a timer that invokes <paramref name="callback"/> every <paramref name="period"/>, without capturing
    /// the caller's execution context.
    /// </summary>
    /// <param name="callback">The method to invoke on each tick.</param>
    /// <param name="period">The due time and period of the timer.</param>
    /// <returns>The started timer.</returns>
    internal static Timer CreateCleanupTimer(TimerCallback callback, TimeSpan period)
    {
        // Without suppressing flow the timer would capture, and keep alive forever, the execution context (AsyncLocal
        // values such as Activity.Current) of whichever caller happens to trigger type initialization. SuppressFlow
        // throws if flow is already suppressed, in which case there is nothing to capture anyway.
        if (ExecutionContext.IsFlowSuppressed())
        {
            return new(callback, null, period, period);
        }

        using (ExecutionContext.SuppressFlow())
        {
            return new(callback, null, period, period);
        }
    }

    /// <summary>
    /// Returns the <see cref="ThrottledLogger"/> associated with the given <paramref name="logger"/>,
    /// creating one if it does not yet exist.
    /// </summary>
    internal static ThrottledLogger GetOrCreate(ILogger logger)
        => Instances.GetOrCreateValue(logger);

    /// <summary>
    /// Timer callback that triggers cleanup of expired entries across all registered throttler instances.
    /// </summary>
    private static void OnCleanupTimer(object? state)
    {
        foreach (var (_, throttler) in Instances)
        {
            throttler.Cleanup();
        }
    }

    /// <summary>
    /// Removes entries that have been idle longer than the configured expiry threshold and whose throttle window has closed.
    /// </summary>
    private void Cleanup()
    {
        var tick = Stopwatch.GetTimestamp();
        var expiry = TimeSpan.FromTicks(Volatile.Read(ref _expiryTicks));

        foreach (var kv in _tracker)
        {
            var entry = kv.Value;

            if (Stopwatch.GetElapsedTime(entry.LastSeenTick, tick) > expiry
                && Stopwatch.GetElapsedTime(entry.LastLogTick, tick) >= entry.Interval)
            {
                // Removes only if the entry is unchanged since it was read, so a concurrent ShouldLog update is never discarded.
                _tracker.TryRemove(kv);
            }
        }
    }
}