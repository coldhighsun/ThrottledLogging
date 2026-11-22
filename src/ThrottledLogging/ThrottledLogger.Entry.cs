namespace ThrottledLogging;

public partial class ThrottledLogger
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
        /// The <see cref="TimeProvider.GetTimestamp"/> timestamp of the most recent call for the key, whether logged or suppressed.
        /// </summary>
        private long _lastSeenTick;

        /// <summary>
        /// The longest throttle interval passed for the key in this window, in <see cref="TimeSpan"/> ticks.
        /// </summary>
        private long _intervalTicks;

        /// <summary>
        /// Initializes a new instance of the <see cref="Entry"/> class for a window that starts at <paramref name="lastLogTick"/>.
        /// </summary>
        /// <param name="lastLogTick">The <see cref="TimeProvider.GetTimestamp"/> timestamp when the key was logged, which starts the window.</param>
        /// <param name="interval">The throttle interval passed by the call that logged the key.</param>
        public Entry(long lastLogTick, TimeSpan interval)
        {
            LastLogTick = lastLogTick;
            _lastSeenTick = lastLogTick;
            _intervalTicks = interval.Ticks;
        }

        /// <summary>
        /// The <see cref="TimeProvider.GetTimestamp"/> timestamp when the key was logged, which started the current window.
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
        /// <param name="tick">The <see cref="TimeProvider.GetTimestamp"/> timestamp of the call.</param>
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
        /// <param name="tick">The current timestamp, from <paramref name="timeProvider"/>.</param>
        /// <param name="expiry">How long the entry must have been idle.</param>
        /// <param name="timeProvider">The provider that produced the entry's timestamps and <paramref name="tick"/>.</param>
        /// <returns>
        /// <see langword="true"/> if the entry was expired and this call sealed it; <see langword="false"/> if it is still
        /// in use, was updated concurrently, or was already sealed.
        /// </returns>
        public bool TrySealIfExpired(long tick, TimeSpan expiry, TimeProvider timeProvider)
        {
            // The count is read first: if a suppressed call counts after this point, the compare-exchange below fails.
            var count = Volatile.Read(ref _suppressedCount);
            if (count == Sealed)
            {
                return false;
            }

            var lastSeenTick = Volatile.Read(ref _lastSeenTick);

            return timeProvider.GetElapsedTime(lastSeenTick, tick) > expiry
                   && timeProvider.GetElapsedTime(LastLogTick, tick) >= Interval
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
}
