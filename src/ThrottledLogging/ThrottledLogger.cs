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
    private struct Entry
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

        /// <summary>The <see cref="Stopwatch"/> timestamp when the key was last logged.</summary>
        public long LastLogTick { get; }

        /// <summary>The number of log calls suppressed since the last successful log.</summary>
        public int SuppressedCount { get; }
    }

    /// <summary>
    /// Represents the timer used to schedule periodic cleanup operations.
    /// </summary>
    private static readonly Timer CleanupTimer;

    /// <summary>
    /// A thread-safe mapping of <see cref="ILogger"/> instances to their corresponding <see cref="ThrottledLogger"/> instances,
    /// </summary>
    private static readonly ConditionalWeakTable<ILogger, ThrottledLogger> Instances = new ConditionalWeakTable<ILogger, ThrottledLogger>();

    /// <summary>
    /// The age threshold (in stopwatch ticks) after which a log entry is considered expired and eligible for cleanup.
    /// </summary>
    private static long _expiryTick;

    /// <summary>
    /// A thread-safe dictionary that tracks log keys and their associated log entry data (last log timestamp and suppressed count) for this throttler instance.
    /// </summary>
    private readonly ConcurrentDictionary<string, Entry> _tracker = new ConcurrentDictionary<string, Entry>();

    /// <summary>
    /// Initializes static members of the <see cref="ThrottledLogger"/> class, setting up the default cleanup period and starting the background timer for cleanup of expired entries.
    /// </summary>
    static ThrottledLogger()
    {
        var defaultCleanupPeriod = TimeSpan.FromHours(1);

        _expiryTick = defaultCleanupPeriod.Ticks;
        CleanupTimer = new(OnCleanupTimer, null, defaultCleanupPeriod, defaultCleanupPeriod);
    }

    /// <summary>
    /// Configures the global expiry threshold and cleanup timer period for all <see cref="ThrottledLogger"/> instances.
    /// </summary>
    /// <param name="expiry">How long an entry must be idle before it is eligible for cleanup.</param>
    /// <param name="cleanupPeriod">How often the background cleanup timer runs.</param>
    public static void Configure(TimeSpan expiry, TimeSpan cleanupPeriod)
    {
        _expiryTick = expiry.Ticks;
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

        if (_tracker.TryGetValue(key, out var entry))
        {
            if (tick - entry.LastLogTick < interval.Ticks)
            {
                _tracker[key] = new Entry(entry.LastLogTick, entry.SuppressedCount + 1);
                suppressedCount = 0;
                return false;
            }
        }

        suppressedCount = _tracker.TryGetValue(key, out var prev) ? prev.SuppressedCount : 0;
        _tracker[key] = new Entry(tick, 0);
        return true;
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
#if !NETSTANDARD2_0
        foreach (var (_, throttler) in Instances)
        {
            throttler.Cleanup();
        }
#endif
    }

    /// <summary>
    /// Removes entries from the tracker whose age exceeds the configured expiry threshold.
    /// </summary>
    private void Cleanup()
    {
        var tick = Stopwatch.GetTimestamp();

        var expiredKeys = _tracker
            .Where(kv => tick - kv.Value.LastLogTick > _expiryTick)
            .Select(kv => kv.Key);

        foreach (var k in expiredKeys)
        {
            _tracker.TryRemove(k, out _);
        }
    }
}
