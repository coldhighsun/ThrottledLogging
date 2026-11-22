namespace ThrottledLogging;

public partial class ThrottledLogger
{
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
    /// The age threshold, in <see cref="TimeSpan"/> ticks, after which a log entry is considered expired and eligible for cleanup.
    /// Stored as a <see cref="long"/> and accessed through <see cref="Volatile"/>, so the cleanup thread never reads a torn value.
    /// </summary>
    private static long _expiryTicks;

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
    /// Timer callback that triggers cleanup of expired entries across all registered throttler instances.
    /// </summary>
    private static void OnCleanupTimer(object? state)
    {
        var expiry = TimeSpan.FromTicks(Volatile.Read(ref _expiryTicks));

        foreach (var (_, throttler) in Instances)
        {
            throttler.Cleanup(expiry);
        }
    }

    /// <summary>
    /// Removes entries that have been idle longer than <paramref name="expiry"/> and whose throttle window has closed.
    /// </summary>
    /// <param name="expiry">How long an entry must have been idle to be removed.</param>
    internal void Cleanup(TimeSpan expiry)
    {
        var tick = _timeProvider.GetTimestamp();

        foreach (var kv in _tracker)
        {
            // Sealing fails if the entry was updated since it was read, so a concurrently refreshed entry is never discarded.
            if (kv.Value.TrySealIfExpired(tick, expiry, _timeProvider))
            {
                _tracker.TryRemove(kv);
            }
        }
    }
}
