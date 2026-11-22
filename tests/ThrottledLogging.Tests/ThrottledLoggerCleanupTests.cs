using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace ThrottledLogging.Tests;

/// <summary>
/// Tests for the expiry cleanup, <see cref="ThrottledLogger.Configure(TimeSpan, TimeSpan)"/> and the cleanup timer of <see cref="ThrottledLogger"/>.
/// </summary>
[Collection("Sequential")]
public class ThrottledLoggerCleanupTests
{
    /// <summary>
    /// Verifies that cleanup removes an entry once it is idle beyond the expiry and its throttle window has closed.
    /// </summary>
    [Fact]
    public void Cleanup_IdleEntryWhoseIntervalHasElapsed_RemovesEntry()
    {
        var time = new FakeTimeProvider();
        var throttler = new ThrottledLogger(time);
        throttler.ShouldLog("key", TimeSpan.FromMilliseconds(20), out _); // entry created

        time.Advance(TimeSpan.FromMilliseconds(100)); // the interval has elapsed and the entry is idle beyond the expiry
        throttler.Cleanup(TimeSpan.FromMilliseconds(1));

        Assert.False(throttler.TryGetSuppressedCount("key", out _));
    }

    /// <summary>
    /// Verifies that cleanup keeps an entry that has not been idle longer than the expiry.
    /// </summary>
    [Fact]
    public void Cleanup_EntryNotIdleBeyondExpiry_KeepsEntry()
    {
        var time = new FakeTimeProvider();
        var throttler = new ThrottledLogger(time);
        throttler.ShouldLog("key", TimeSpan.FromMilliseconds(20), out _); // entry created

        time.Advance(TimeSpan.FromMilliseconds(100));
        throttler.Cleanup(TimeSpan.FromMinutes(1));

        Assert.True(throttler.TryGetSuppressedCount("key", out _));
    }

    /// <summary>
    /// Verifies that an expiry shorter than the throttle interval does not let cleanup evict an entry
    /// whose throttle window is still open.
    /// </summary>
    [Fact]
    public void Cleanup_ExpiryShorterThanInterval_DoesNotBreakThrottling()
    {
        var time = new FakeTimeProvider();
        var throttler = new ThrottledLogger(time);
        var interval = TimeSpan.FromDays(1);
        throttler.ShouldLog("key", interval, out _); // allowed
        throttler.ShouldLog("key", interval, out _); // suppressed (count=1)

        time.Advance(TimeSpan.FromMilliseconds(200)); // the window is still open, but the entry is idle beyond the expiry
        throttler.Cleanup(TimeSpan.FromMilliseconds(1));

        Assert.False(throttler.ShouldLog("key", interval, out _)); // must still be suppressed (count=2)
        Assert.True(throttler.TryGetSuppressedCount("key", out var suppressed));
        Assert.Equal(2, suppressed);
    }

    /// <summary>
    /// Verifies that a shorter interval passed by a later suppressed call does not shrink the stored
    /// throttle window, so cleanup keeps the entry while the longer window is still open.
    /// </summary>
    [Fact]
    public void Cleanup_ShorterIntervalOnSuppressedCall_KeepsLongerWindow()
    {
        var time = new FakeTimeProvider();
        var throttler = new ThrottledLogger(time);
        throttler.ShouldLog("key", TimeSpan.FromDays(1), out _); // allowed
        throttler.ShouldLog("key", TimeSpan.FromMilliseconds(100), out _); // suppressed (count=1)

        time.Advance(TimeSpan.FromMilliseconds(300)); // the 100 ms window has closed, but the 1-day window is still open
        throttler.Cleanup(TimeSpan.FromMilliseconds(1));

        Assert.True(throttler.TryGetSuppressedCount("key", out var suppressed));
        Assert.Equal(1, suppressed);
    }

    /// <summary>
    /// Verifies that cleanup measures idle time from the most recent call (including suppressed ones),
    /// not from the last emitted log, so a recently suppressed key is kept after its interval elapses.
    /// </summary>
    [Fact]
    public void Cleanup_RecentlySuppressedEntry_NotRemovedAfterIntervalElapses()
    {
        var time = new FakeTimeProvider();
        var throttler = new ThrottledLogger(time);
        var interval = TimeSpan.FromSeconds(3);
        var expiry = TimeSpan.FromSeconds(3);

        throttler.ShouldLog("key", interval, out _); // allowed at t=0
        time.Advance(TimeSpan.FromSeconds(1.5));
        throttler.ShouldLog("key", interval, out _); // suppressed at t=1.5s, refreshes idle time

        // At t=3.5s the interval has elapsed and the last log is older than the expiry,
        // but the key was seen only 2s ago, so it is not idle yet and must be kept.
        time.Advance(TimeSpan.FromSeconds(2));
        throttler.Cleanup(expiry);

        Assert.True(throttler.TryGetSuppressedCount("key", out var suppressed));
        Assert.Equal(1, suppressed);
    }

    /// <summary>
    /// Cleanup periods that <see cref="ThrottledLogger.Configure(TimeSpan, TimeSpan)"/> must reject: zero, periods under
    /// 1 millisecond that the timer truncates to zero, negative values other than <see cref="Timeout.InfiniteTimeSpan"/>,
    /// and values beyond the longest period a timer supports.
    /// </summary>
    public static TheoryData<TimeSpan> InvalidCleanupPeriods =>
    [
        TimeSpan.Zero,
        TimeSpan.FromMicroseconds(500),
        TimeSpan.FromMilliseconds(-2),
        TimeSpan.FromSeconds(-1),
        TimeSpan.FromMilliseconds(uint.MaxValue),
        TimeSpan.FromDays(60),
    ];

    /// <summary>
    /// Verifies that a negative expiry is rejected.
    /// </summary>
    [Fact]
    public void Configure_NegativeExpiry_ThrowsArgumentOutOfRangeException()
    {
        try
        {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => ThrottledLogger.Configure(expiry: TimeSpan.FromSeconds(-1), cleanupPeriod: TimeSpan.FromHours(1)));

            Assert.Equal("expiry", exception.ParamName);
        }
        finally
        {
            ThrottledLogger.Configure(expiry: TimeSpan.FromHours(1), cleanupPeriod: TimeSpan.FromHours(1));
        }
    }

    /// <summary>
    /// Verifies that a cleanup period the timer cannot run periodically with is rejected, reporting the <c>cleanupPeriod</c> parameter.
    /// </summary>
    /// <param name="cleanupPeriod">The invalid cleanup period to pass.</param>
    [Theory]
    [MemberData(nameof(InvalidCleanupPeriods))]
    public void Configure_InvalidCleanupPeriod_ThrowsArgumentOutOfRangeException(TimeSpan cleanupPeriod)
    {
        try
        {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(
                () => ThrottledLogger.Configure(expiry: TimeSpan.FromHours(1), cleanupPeriod: cleanupPeriod));

            Assert.Equal("cleanupPeriod", exception.ParamName);
        }
        finally
        {
            ThrottledLogger.Configure(expiry: TimeSpan.FromHours(1), cleanupPeriod: TimeSpan.FromHours(1));
        }
    }

    /// <summary>
    /// Verifies that the shortest and longest cleanup periods the timer supports are accepted.
    /// </summary>
    /// <param name="milliseconds">The cleanup period, in milliseconds.</param>
    [Theory]
    [InlineData(1d)]
    [InlineData(uint.MaxValue - 1d)]
    public void Configure_BoundaryCleanupPeriod_DoesNotThrow(double milliseconds)
    {
        try
        {
            var exception = Record.Exception(
                () => ThrottledLogger.Configure(expiry: TimeSpan.FromHours(1), cleanupPeriod: TimeSpan.FromMilliseconds(milliseconds)));

            Assert.Null(exception);
        }
        finally
        {
            ThrottledLogger.Configure(expiry: TimeSpan.FromHours(1), cleanupPeriod: TimeSpan.FromHours(1));
        }
    }

    /// <summary>
    /// Verifies that a rejected call leaves the previously configured settings in effect.
    /// </summary>
    [Fact]
    public void Configure_InvalidCleanupPeriod_KeepsPreviousSettings()
    {
        ThrottledLogger.Configure(expiry: TimeSpan.FromMilliseconds(1), cleanupPeriod: TimeSpan.FromMilliseconds(50));
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => ThrottledLogger.Configure(expiry: TimeSpan.FromHours(1), cleanupPeriod: TimeSpan.Zero));

            var logger = new FakeLogger();
            logger.LogInformationThrottled("key", TimeSpan.FromMilliseconds(20), "Msg"); // entry created

            Thread.Sleep(200); // the short expiry and period must still apply, so cleanup removes the entry

            Assert.False(logger.TryGetThrottledSuppressedCount("key", out _));
        }
        finally
        {
            ThrottledLogger.Configure(expiry: TimeSpan.FromHours(1), cleanupPeriod: TimeSpan.FromHours(1));
        }
    }

    /// <summary>
    /// Verifies that <see cref="Timeout.InfiniteTimeSpan"/> is accepted as the cleanup period and disables cleanup.
    /// </summary>
    [Fact]
    public void Configure_InfiniteCleanupPeriod_DisablesCleanup()
    {
        try
        {
            ThrottledLogger.Configure(expiry: TimeSpan.FromMilliseconds(1), cleanupPeriod: Timeout.InfiniteTimeSpan);

            var logger = new FakeLogger();
            logger.LogInformationThrottled("key", TimeSpan.FromMilliseconds(20), "Msg"); // entry created

            Thread.Sleep(200); // the entry is expired and its window has closed, but cleanup never runs

            Assert.True(logger.TryGetThrottledSuppressedCount("key", out _));
        }
        finally
        {
            ThrottledLogger.Configure(expiry: TimeSpan.FromHours(1), cleanupPeriod: TimeSpan.FromHours(1));
        }
    }

    /// <summary>
    /// Verifies that creating the cleanup timer while execution context flow is already suppressed does not throw,
    /// and leaves flow suppressed for the caller.
    /// </summary>
    [Fact]
    public void CreateCleanupTimer_FlowAlreadySuppressed_DoesNotThrowAndLeavesFlowSuppressed()
    {
        using (ExecutionContext.SuppressFlow())
        {
            using var timer = ThrottledLogger.CreateCleanupTimer(static _ => { }, Timeout.InfiniteTimeSpan);

            Assert.True(ExecutionContext.IsFlowSuppressed());
        }
    }

    /// <summary>
    /// Verifies that creating the cleanup timer while execution context flow is not suppressed restores flow afterwards.
    /// </summary>
    [Fact]
    public void CreateCleanupTimer_FlowNotSuppressed_RestoresFlow()
    {
        using var timer = ThrottledLogger.CreateCleanupTimer(static _ => { }, Timeout.InfiniteTimeSpan);

        Assert.False(ExecutionContext.IsFlowSuppressed());
    }
}
