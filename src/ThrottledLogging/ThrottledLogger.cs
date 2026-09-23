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
    /// Represents a tracked log entry with its last log timestamp and the number of suppressed occurrences.
    /// </summary>
    private struct Entry : IEquatable<Entry>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Entry"/> struct with the specified last log timestamp and suppressed count.
        /// </summary>
        /// <param name="lastLogTick">The <see cref="Stopwatch"/> timestamp when the key was last logged.</param>
        /// <param name="suppressedCount">The number of log calls suppressed since the last successful log.</param>
        public Entry(long lastLogTick, int suppressedCount)
        {
            LastLogTick = lastLogTick;
            SuppressedCount = suppressedCount;
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
        /// Determines whether this instance and another <see cref="Entry"/> have the same field values.
        /// </summary>
        /// <param name="other">The other <see cref="Entry"/> to compare against.</param>
        /// <returns><see langword="true"/> if both instances have equal <see cref="LastLogTick"/> and <see cref="SuppressedCount"/> values; otherwise <see langword="false"/>.</returns>
        /// <remarks>
        /// Implementing <see cref="IEquatable{T}"/> lets <see cref="ConcurrentDictionary{TKey, TValue}.TryUpdate(TKey, TValue, TValue)"/>
        /// use the non-boxing generic comparer instead of falling back to a boxing <see cref="object.Equals(object?)"/> comparison.
        /// </remarks>
        public bool Equals(Entry other) => LastLogTick == other.LastLogTick && SuppressedCount == other.SuppressedCount;

        /// <summary>
        /// Determines whether this instance and a specified object, which must also be an <see cref="Entry"/>, have the same field values.
        /// </summary>
        /// <param name="obj">The object to compare with the current instance.</param>
        /// <returns><see langword="true"/> if <paramref name="obj"/> is an <see cref="Entry"/> equal to this instance; otherwise <see langword="false"/>.</returns>
        public override bool Equals(object? obj) => obj is Entry other && Equals(other);

        /// <summary>
        /// Returns a hash code based on <see cref="LastLogTick"/> and <see cref="SuppressedCount"/>.
        /// </summary>
        /// <returns>A hash code for the current instance.</returns>
        public override int GetHashCode() => HashCode.Combine(LastLogTick, SuppressedCount);
    }

    /// <summary>
    /// Represents the timer used to schedule periodic cleanup operations.
    /// </summary>
    private static readonly Timer CleanupTimer;

    /// <summary>
    /// A thread-safe mapping of <see cref="ILogger"/> instances to their corresponding <see cref="ThrottledLogger"/> instances,
    /// </summary>
    private static readonly ConditionalWeakTable<ILogger, ThrottledLogger> Instances = new();

    /// <summary>
    /// The age threshold, as a <see cref="TimeSpan"/>, after which a log entry is considered expired and eligible for cleanup.
    /// </summary>
    private static TimeSpan _expiry;

    /// <summary>
    /// A thread-safe dictionary that tracks log keys and their associated log entry data (last log timestamp and suppressed count) for this throttler instance.
    /// </summary>
    private readonly ConcurrentDictionary<string, Entry> _tracker = new();

    /// <summary>
    /// Initializes static members of the <see cref="ThrottledLogger"/> class, setting up the default cleanup period and starting the background timer for cleanup of expired entries.
    /// </summary>
    static ThrottledLogger()
    {
        var defaultCleanupPeriod = TimeSpan.FromHours(1);

        _expiry = defaultCleanupPeriod;
        CleanupTimer = new(OnCleanupTimer, null, defaultCleanupPeriod, defaultCleanupPeriod);
    }

    /// <summary>
    /// Configures the global expiry threshold and cleanup timer period for all <see cref="ThrottledLogger"/> instances.
    /// </summary>
    /// <param name="expiry">How long an entry must be idle before it is eligible for cleanup.</param>
    /// <param name="cleanupPeriod">How often the background cleanup timer runs.</param>
    public static void Configure(TimeSpan expiry, TimeSpan cleanupPeriod)
    {
        _expiry = expiry;
        CleanupTimer.Change(cleanupPeriod, cleanupPeriod);
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
                if (Stopwatch.GetElapsedTime(existing.LastLogTick, tick) < interval)
                {
                    if (_tracker.TryUpdate(key, new Entry(existing.LastLogTick, existing.SuppressedCount + 1), existing))
                    {
                        suppressedCount = 0;
                        return false;
                    }

                    continue;
                }

                if (_tracker.TryUpdate(key, new Entry(tick, 0), existing))
                {
                    suppressedCount = existing.SuppressedCount;
                    return true;
                }

                continue;
            }

            if (_tracker.TryAdd(key, new Entry(tick, 0)))
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
    /// Removes entries from the tracker whose age exceeds the configured expiry threshold.
    /// </summary>
    private void Cleanup()
    {
        var tick = Stopwatch.GetTimestamp();
        List<string>? expiredKeys = null;

        foreach (var kv in _tracker)
        {
            if (Stopwatch.GetElapsedTime(kv.Value.LastLogTick, tick) > _expiry)
            {
                (expiredKeys ??= new List<string>()).Add(kv.Key);
            }
        }

        if (expiredKeys is null)
        {
            return;
        }

        foreach (var k in expiredKeys)
        {
            _tracker.TryRemove(k, out _);
        }
    }
}