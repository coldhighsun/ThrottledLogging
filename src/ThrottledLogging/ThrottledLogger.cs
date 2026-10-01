using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace ThrottledLogging;

/// <summary>
/// Provides rate-limited logging by suppressing duplicate log entries within a configurable time interval.
/// Tracks log keys and their last-logged timestamps, automatically cleaning up expired entries via a background timer.
/// </summary>
public class ThrottledLogger
{
    /// <summary>
    /// Represents a tracked log entry: the start of its throttle window, the number of suppressed occurrences in that window,
    /// the timestamp of the most recent call, and the longest throttle interval used in that window.
    /// </summary>
    /// <remarks>
    /// An entry is a mutable reference type so that suppressed calls, the common case, update it in place without allocating.
    /// Its suppressed count doubles as the linearization point: a thread that starts a new window, or cleanup that removes
    /// the entry, first seals it (see <see cref="TrySeal"/>), after which no suppressed call can be recorded on it and
    /// callers that find it sealed retry against the entry that replaced it.
    /// </remarks>
    private sealed class Entry
    {
        /// <summary>
        /// The value of <see cref="_suppressedCount"/> once the entry is sealed. Real counts are never negative.
        /// </summary>
        private const int Sealed = -1;

        /// <summary>
        /// The number of log calls suppressed since the window started, or <see cref="Sealed"/>.
        /// </summary>
        private int _suppressedCount;

        /// <summary>
        /// The <see cref="Stopwatch"/> timestamp of the most recent call for the key, whether logged or suppressed.
        /// </summary>
        private long _lastSeenTick;

        /// <summary>
        /// The longest throttle interval passed for the key in this window, in <see cref="TimeSpan"/> ticks.
        /// </summary>
        private long _intervalTicks;

        /// <summary>
        /// Initializes a new instance of the <see cref="Entry"/> class for a window that starts at <paramref name="lastLogTick"/>.
        /// </summary>
        /// <param name="lastLogTick">The <see cref="Stopwatch"/> timestamp when the key was logged, which starts the window.</param>
        /// <param name="interval">The throttle interval passed by the call that logged the key.</param>
        public Entry(long lastLogTick, TimeSpan interval)
        {
            LastLogTick = lastLogTick;
            _lastSeenTick = lastLogTick;
            _intervalTicks = interval.Ticks;
        }

        /// <summary>
        /// The <see cref="Stopwatch"/> timestamp when the key was logged, which started the current window.
        /// </summary>
        public long LastLogTick
        {
            get;
        }

        /// <summary>
        /// The longest throttle interval passed for the key in this window.
        /// </summary>
        public TimeSpan Interval => TimeSpan.FromTicks(Volatile.Read(ref _intervalTicks));

        /// <summary>
        /// The number of log calls suppressed in this window, or 0 if the entry is sealed.
        /// </summary>
        public int SuppressedCount => Math.Max(Volatile.Read(ref _suppressedCount), 0);

        /// <summary>
        /// Records a suppressed call, unless the entry has been sealed.
        /// </summary>
        /// <param name="tick">The <see cref="Stopwatch"/> timestamp of the call.</param>
        /// <param name="interval">The throttle interval passed by the call.</param>
        /// <returns>
        /// <see langword="true"/> if the call was recorded on this entry; <see langword="false"/> if the entry is sealed
        /// and the caller must look the key up again.
        /// </returns>
        public bool TrySuppress(long tick, TimeSpan interval)
        {
            // The timestamps are updated before the count, so cleanup, which seals by compare-exchanging the count it
            // read before them, never seals an entry on stale timestamps once this call has counted.
            RaiseTo(ref _lastSeenTick, tick);
            RaiseTo(ref _intervalTicks, interval.Ticks);

            var count = Volatile.Read(ref _suppressedCount);

            while (true)
            {
                if (count == Sealed)
                {
                    return false;
                }

                var next = IncrementSaturating(count);
                if (next == count)
                {
                    return true;
                }

                var previous = Interlocked.CompareExchange(ref _suppressedCount, next, count);
                if (previous == count)
                {
                    return true;
                }

                count = previous;
            }
        }

        /// <summary>
        /// Seals the entry so that no further suppressed call can be recorded on it.
        /// </summary>
        /// <param name="suppressedCount">The number of calls suppressed in this window, if the call sealed the entry.</param>
        /// <returns><see langword="true"/> if this call sealed the entry; <see langword="false"/> if it was already sealed.</returns>
        public bool TrySeal(out int suppressedCount)
        {
            var count = Interlocked.Exchange(ref _suppressedCount, Sealed);

            suppressedCount = Math.Max(count, 0);
            return count != Sealed;
        }

        /// <summary>
        /// Seals the entry if it has been idle longer than <paramref name="expiry"/> and its throttle window has closed.
        /// </summary>
        /// <param name="tick">The current <see cref="Stopwatch"/> timestamp.</param>
        /// <param name="expiry">How long the entry must have been idle.</param>
        /// <returns>
        /// <see langword="true"/> if the entry was expired and this call sealed it; <see langword="false"/> if it is still
        /// in use, was updated concurrently, or was already sealed.
        /// </returns>
        public bool TrySealIfExpired(long tick, TimeSpan expiry)
        {
            // The count is read first: if a suppressed call counts after this point, the compare-exchange below fails.
            var count = Volatile.Read(ref _suppressedCount);
            if (count == Sealed)
            {
                return false;
            }

            var lastSeenTick = Volatile.Read(ref _lastSeenTick);

            return Stopwatch.GetElapsedTime(lastSeenTick, tick) > expiry
                   && Stopwatch.GetElapsedTime(LastLogTick, tick) >= Interval
                   && Interlocked.CompareExchange(ref _suppressedCount, Sealed, count) == count;
        }

        /// <summary>
        /// Atomically raises <paramref name="location"/> to <paramref name="value"/> if it is currently lower.
        /// </summary>
        /// <param name="location">The field to raise.</param>
        /// <param name="value">The value to raise it to.</param>
        private static void RaiseTo(ref long location, long value)
        {
            var current = Volatile.Read(ref location);

            while (value > current)
            {
                var previous = Interlocked.CompareExchange(ref location, value, current);
                if (previous == current)
                {
                    return;
                }

                current = previous;
            }
        }
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
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public bool ShouldLog(string key, TimeSpan interval, out int suppressedCount)
    {
        ArgumentNullException.ThrowIfNull(key);

        var tick = Stopwatch.GetTimestamp();

        while (true)
        {
            if (!_tracker.TryGetValue(key, out var existing))
            {
                if (_tracker.TryAdd(key, new Entry(tick, interval)))
                {
                    suppressedCount = 0;
                    return true;
                }

                continue;
            }

            // A non-positive interval always logs. Checked explicitly because the elapsed time is negative when a
            // concurrent call with a later timestamp has already updated the entry.
            if (interval > TimeSpan.Zero && Stopwatch.GetElapsedTime(existing.LastLogTick, tick) < interval)
            {
                if (existing.TrySuppress(tick, interval))
                {
                    suppressedCount = 0;
                    return false;
                }

                // The entry was sealed by a call that is replacing it, so look the key up again.
                continue;
            }

            // Allocated before sealing, so nothing that can throw runs between sealing the old entry and replacing it.
            var next = new Entry(Math.Max(existing.LastLogTick, tick), interval);

            // Sealing hands the suppressed count to exactly one caller, which then replaces the entry.
            if (!existing.TrySeal(out var pending))
            {
                continue;
            }

            if (_tracker.TryUpdate(key, next, existing))
            {
                suppressedCount = pending;
                return true;
            }

            // The sealed entry was removed by Reset or cleanup, which discards its count; start over from scratch.
            if (_tracker.TryAdd(key, next))
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
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public void Reset(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        _tracker.TryRemove(key, out _);
    }

    /// <summary>
    /// Attempts to get the number of log calls currently suppressed for <paramref name="key"/>.
    /// </summary>
    /// <param name="key">The throttling key to query.</param>
    /// <param name="suppressedCount">The number of suppressed calls recorded for the key, if tracked.</param>
    /// <returns><see langword="true"/> if the key is currently tracked; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    public bool TryGetSuppressedCount(string key, out int suppressedCount)
    {
        ArgumentNullException.ThrowIfNull(key);

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
    /// Gets the <see cref="ThrottledLogger"/> associated with the given <paramref name="logger"/>, without creating one.
    /// </summary>
    /// <param name="logger">The logger whose throttler to get.</param>
    /// <param name="throttler">The associated throttler, if one exists.</param>
    /// <returns><see langword="true"/> if a throttler exists for <paramref name="logger"/>; otherwise <see langword="false"/>.</returns>
    internal static bool TryGet(ILogger logger, [NotNullWhen(true)] out ThrottledLogger? throttler)
        => Instances.TryGetValue(logger, out throttler);

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
            // Sealing fails if the entry was updated since it was read, so a concurrently refreshed entry is never discarded.
            if (kv.Value.TrySealIfExpired(tick, expiry))
            {
                _tracker.TryRemove(kv);
            }
        }
    }
}
