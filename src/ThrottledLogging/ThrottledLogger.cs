using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using ThrottledLogging.Resources;

namespace ThrottledLogging;

/// <summary>
/// Provides rate-limited logging by suppressing duplicate log entries within a configurable time interval.
/// Tracks log keys and their last-logged timestamps, automatically cleaning up expired entries via a background timer.
/// </summary>
public partial class ThrottledLogger
{
    /// <summary>
    /// A thread-safe mapping of <see cref="ILogger"/> instances to their corresponding <see cref="ThrottledLogger"/> instances,
    /// </summary>
    private static readonly ConditionalWeakTable<ILogger, ThrottledLogger> Instances = new();

    /// <summary>
    /// A thread-safe dictionary that tracks log keys and their associated log entry data (last log timestamp and suppressed count) for this throttler instance.
    /// </summary>
    private readonly ConcurrentDictionary<string, Entry> _tracker = new();

    /// <summary>
    /// The provider of the timestamps that start and measure throttle windows.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThrottledLogger"/> class that measures time with <see cref="TimeProvider.System"/>.
    /// </summary>
    public ThrottledLogger()
        : this(TimeProvider.System)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ThrottledLogger"/> class that measures time with <paramref name="timeProvider"/>.
    /// </summary>
    /// <param name="timeProvider">The provider of the timestamps used to start and measure throttle windows.</param>
    /// <exception cref="ArgumentNullException"><paramref name="timeProvider"/> is <see langword="null"/>.</exception>
    public ThrottledLogger(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Pins the language used to render the suppressed-count suffix (e.g. "(3 messages suppressed)"), overriding the
    /// current thread's <see cref="CultureInfo.CurrentUICulture"/>. The default, <see langword="null"/>, follows
    /// <see cref="CultureInfo.CurrentUICulture"/> automatically. This setting is global and shared by all
    /// <see cref="ThrottledLogger"/> instances.
    /// </summary>
    public static CultureInfo? Language
    {
        get => Messages.Culture;
        set => Messages.Culture = value;
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

        var tick = _timeProvider.GetTimestamp();

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
            if (interval > TimeSpan.Zero && _timeProvider.GetElapsedTime(existing.LastLogTick, tick) < interval)
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
    /// Associates <paramref name="throttler"/> with <paramref name="logger"/>, replacing any existing association,
    /// so the throttled logging extension methods use it. Lets tests supply a throttler with a controlled clock.
    /// </summary>
    /// <param name="logger">The logger to associate the throttler with.</param>
    /// <param name="throttler">The throttler the extension methods should use for <paramref name="logger"/>.</param>
    internal static void Register(ILogger logger, ThrottledLogger throttler)
        => Instances.AddOrUpdate(logger, throttler);
}
