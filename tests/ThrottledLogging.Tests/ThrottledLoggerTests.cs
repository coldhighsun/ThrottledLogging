using Xunit;

namespace ThrottledLogging.Tests;

[Collection("Sequential")]
public class ThrottledLoggerTests
{
    /// <summary>
    /// Verifies that cleanup removes an entry once it is idle beyond the expiry and its throttle window has closed.
    /// </summary>
    [Fact]
    public void Configure_ShorterExpiry_CleanupRemovesIdleEntriesWhoseIntervalHasElapsed()
    {
        ThrottledLogger.Configure(expiry: TimeSpan.FromMilliseconds(1), cleanupPeriod: TimeSpan.FromMilliseconds(50));
        try
        {
            var logger = new FakeLogger();
            logger.LogInformationThrottled("key", TimeSpan.FromMilliseconds(20), "Msg"); // entry created

            Thread.Sleep(200); // wait for the interval to elapse and cleanup to run

            var found = logger.TryGetThrottledSuppressedCount("key", out _);

            Assert.False(found);
        }
        finally
        {
            ThrottledLogger.Configure(expiry: TimeSpan.FromHours(1), cleanupPeriod: TimeSpan.FromHours(1));
        }
    }

    /// <summary>
    /// Verifies that an expiry shorter than the throttle interval does not let cleanup evict an entry
    /// whose throttle window is still open.
    /// </summary>
    [Fact]
    public void Configure_ExpiryShorterThanInterval_CleanupDoesNotBreakThrottling()
    {
        ThrottledLogger.Configure(expiry: TimeSpan.FromMilliseconds(1), cleanupPeriod: TimeSpan.FromMilliseconds(50));
        try
        {
            var logger = new FakeLogger();
            logger.LogInformationThrottled("key", TimeSpan.FromDays(1), "Msg"); // allowed
            logger.LogInformationThrottled("key", TimeSpan.FromDays(1), "Msg"); // suppressed (count=1)

            Thread.Sleep(200); // let cleanup run several times while the throttle window is still open

            logger.LogInformationThrottled("key", TimeSpan.FromDays(1), "Msg"); // must still be suppressed (count=2)

            Assert.Single(logger.Entries);
            Assert.True(logger.TryGetThrottledSuppressedCount("key", out var suppressed));
            Assert.Equal(2, suppressed);
        }
        finally
        {
            ThrottledLogger.Configure(expiry: TimeSpan.FromHours(1), cleanupPeriod: TimeSpan.FromHours(1));
        }
    }

    /// <summary>
    /// Verifies that a shorter interval passed by a later suppressed call does not shrink the stored
    /// throttle window, so cleanup keeps the entry while the longer window is still open.
    /// </summary>
    [Fact]
    public void Configure_ShorterIntervalOnSuppressedCall_CleanupKeepsLongerWindow()
    {
        ThrottledLogger.Configure(expiry: TimeSpan.FromMilliseconds(1), cleanupPeriod: TimeSpan.FromMilliseconds(50));
        try
        {
            var logger = new FakeLogger();
            logger.LogInformationThrottled("key", TimeSpan.FromDays(1), "Msg"); // allowed
            logger.LogInformationThrottled("key", TimeSpan.FromMilliseconds(100), "Msg"); // suppressed (count=1)

            Thread.Sleep(300); // the 100 ms window has closed, but the 1-day window is still open

            Assert.True(logger.TryGetThrottledSuppressedCount("key", out var suppressed));
            Assert.Equal(1, suppressed);
        }
        finally
        {
            ThrottledLogger.Configure(expiry: TimeSpan.FromHours(1), cleanupPeriod: TimeSpan.FromHours(1));
        }
    }

    /// <summary>
    /// Verifies that cleanup measures idle time from the most recent call (including suppressed ones),
    /// not from the last emitted log, so a recently suppressed key is kept after its interval elapses.
    /// </summary>
    [Fact]
    public void Configure_RecentlySuppressedEntry_NotRemovedAfterIntervalElapses()
    {
        ThrottledLogger.Configure(expiry: TimeSpan.FromSeconds(3), cleanupPeriod: TimeSpan.FromMilliseconds(20));
        try
        {
            var logger = new FakeLogger();
            var interval = TimeSpan.FromSeconds(3);

            logger.LogInformationThrottled("key", interval, "Msg"); // allowed at t=0
            Thread.Sleep(TimeSpan.FromSeconds(1.5));
            logger.LogInformationThrottled("key", interval, "Msg"); // suppressed at t=1.5s, refreshes idle time

            // At t=3.5s the interval has elapsed and the last log is older than the expiry,
            // but the key was seen only 2s ago, so it is not idle yet and must be kept.
            Thread.Sleep(TimeSpan.FromSeconds(2));

            Assert.True(logger.TryGetThrottledSuppressedCount("key", out var suppressed));
            Assert.Equal(1, suppressed);
        }
        finally
        {
            ThrottledLogger.Configure(expiry: TimeSpan.FromHours(1), cleanupPeriod: TimeSpan.FromHours(1));
        }
    }

    /// <summary>
    /// Verifies that a count below <see cref="int.MaxValue"/> is incremented by one.
    /// </summary>
    [Fact]
    public void IncrementSaturating_BelowMaxValue_IncrementsByOne()
    {
        var result = ThrottledLogger.IncrementSaturating(41);

        Assert.Equal(42, result);
    }

    /// <summary>
    /// Verifies that a count at <see cref="int.MaxValue"/> saturates instead of overflowing to a negative value.
    /// </summary>
    [Fact]
    public void IncrementSaturating_AtMaxValue_DoesNotOverflow()
    {
        var result = ThrottledLogger.IncrementSaturating(int.MaxValue);

        Assert.Equal(int.MaxValue, result);
    }

    [Fact]
    public void ShouldLog_AfterIntervalExpires_ReturnsTrue()
    {
        var throttler = new ThrottledLogger();
        throttler.ShouldLog("key", TimeSpan.FromMilliseconds(20), out _);
        throttler.ShouldLog("key", TimeSpan.FromMilliseconds(20), out _); // suppressed

        Thread.Sleep(50);

        var result = throttler.ShouldLog("key", TimeSpan.FromMilliseconds(20), out var suppressed);

        Assert.True(result);
        Assert.Equal(1, suppressed);
    }

    [Fact]
    public void ShouldLog_DifferentKeys_TrackedIndependently()
    {
        var throttler = new ThrottledLogger();
        throttler.ShouldLog("key-a", TimeSpan.FromDays(1), out _);
        throttler.ShouldLog("key-a", TimeSpan.FromDays(1), out _); // suppresses key-a

        // key-b is unaffected
        var result = throttler.ShouldLog("key-b", TimeSpan.FromDays(1), out var suppressed);

        Assert.True(result);
        Assert.Equal(0, suppressed);
    }

    [Fact]
    public void ShouldLog_NewKey_ReturnsTrue()
    {
        var throttler = new ThrottledLogger();

        var result = throttler.ShouldLog("key", TimeSpan.FromMinutes(1), out var suppressed);

        Assert.True(result);
        Assert.Equal(0, suppressed);
    }

    [Fact]
    public void ShouldLog_SuppressedCount_AccumulatesAndReportedOnNextAllowed()
    {
        var throttler = new ThrottledLogger();
        throttler.ShouldLog("key", TimeSpan.FromDays(1), out _); // first: allowed
        throttler.ShouldLog("key", TimeSpan.FromDays(1), out _); // suppressed (count=1)
        throttler.ShouldLog("key", TimeSpan.FromDays(1), out _); // suppressed (count=2)

        // Zero interval forces allow on next call
        var result = throttler.ShouldLog("key", TimeSpan.Zero, out var suppressed);

        Assert.True(result);
        Assert.Equal(2, suppressed);
    }

    [Fact]
    public void ShouldLog_SuppressedCountResets_AfterBeingReported()
    {
        var throttler = new ThrottledLogger();
        throttler.ShouldLog("key", TimeSpan.FromDays(1), out _); // allowed
        throttler.ShouldLog("key", TimeSpan.FromDays(1), out _); // suppressed

        throttler.ShouldLog("key", TimeSpan.Zero, out _); // allowed, reports count=1

        // Next allowed call should report 0 suppressed
        var result = throttler.ShouldLog("key", TimeSpan.Zero, out var suppressed);
        Assert.True(result);
        Assert.Equal(0, suppressed);
    }

    [Fact]
    public void ShouldLog_WithinInterval_ReturnsFalse()
    {
        var throttler = new ThrottledLogger();
        throttler.ShouldLog("key", TimeSpan.FromDays(1), out _);

        var result = throttler.ShouldLog("key", TimeSpan.FromDays(1), out var suppressed);

        Assert.False(result);
        Assert.Equal(0, suppressed);
    }

    [Fact]
    public void ShouldLog_ConcurrentCallsOnSameKey_NoLostUpdates()
    {
        var throttler = new ThrottledLogger();
        const int threadCount = 8;
        const int callsPerThread = 2000;
        var trueCount = 0;

        Parallel.For(0, threadCount, _ =>
        {
            for (var i = 0; i < callsPerThread; i++)
            {
                if (throttler.ShouldLog("key", TimeSpan.FromDays(1), out _))
                {
                    Interlocked.Increment(ref trueCount);
                }
            }
        });

        // Only the very first call across all threads should be allowed; every other
        // call races against the same CAS-retry loop in ShouldLog and must be counted
        // as suppressed exactly once, with no updates lost to the race.
        Assert.Equal(1, trueCount);

        var found = throttler.TryGetSuppressedCount("key", out var suppressed);
        Assert.True(found);
        Assert.Equal((threadCount * callsPerThread) - 1, suppressed);
    }

    [Fact]
    public void ShouldLog_ConcurrentCallsWithExpiringInterval_SuppressedCountsAreNeverLostOrDuplicated()
    {
        var throttler = new ThrottledLogger();
        var interval = TimeSpan.FromMilliseconds(5);
        const int threadCount = 8;
        const int callsPerThread = 2000;

        var falseCount = 0;
        var reportedSuppressedSum = 0;

        Parallel.For(0, threadCount, _ =>
        {
            for (var i = 0; i < callsPerThread; i++)
            {
                if (throttler.ShouldLog("key", interval, out var suppressed))
                {
                    Interlocked.Add(ref reportedSuppressedSum, suppressed);
                }
                else
                {
                    Interlocked.Increment(ref falseCount);
                }
            }
        });

        throttler.TryGetSuppressedCount("key", out var leftover);

        // The short interval forces many concurrent transitions through the "resume after
        // expiry" CAS branch in ShouldLog, not just the "still throttled" branch. Every
        // suppressed (false) call must be accounted for exactly once, either by a later
        // "true" call reporting it or by the leftover count still tracked at the end -
        // regardless of how many threads raced through the reset at the same time.
        Assert.Equal(falseCount, reportedSuppressedSum + leftover);
    }

    [Fact]
    public void ShouldLog_ZeroInterval_AlwaysReturnsTrue()
    {
        var throttler = new ThrottledLogger();

        throttler.ShouldLog("key", TimeSpan.Zero, out _);
        var result = throttler.ShouldLog("key", TimeSpan.Zero, out var suppressed);

        Assert.True(result);
        Assert.Equal(0, suppressed);
    }

    /// <summary>
    /// Verifies that a negative interval is treated like a zero interval and never suppresses.
    /// </summary>
    [Fact]
    public void ShouldLog_NegativeInterval_AlwaysReturnsTrue()
    {
        var throttler = new ThrottledLogger();

        throttler.ShouldLog("key", TimeSpan.FromSeconds(-1), out _);
        var result = throttler.ShouldLog("key", TimeSpan.FromSeconds(-1), out var suppressed);

        Assert.True(result);
        Assert.Equal(0, suppressed);
    }

    /// <summary>
    /// Verifies that concurrent zero-interval calls are never suppressed, even when a call observes an entry
    /// already updated by another call with a later timestamp.
    /// </summary>
    [Fact]
    public void ShouldLog_ConcurrentZeroIntervalCalls_NeverSuppressed()
    {
        var throttler = new ThrottledLogger();
        var suppressedCalls = 0;

        Parallel.For(0, 100_000, _ =>
        {
            if (!throttler.ShouldLog("key", TimeSpan.Zero, out _))
            {
                Interlocked.Increment(ref suppressedCalls);
            }
        });

        Assert.Equal(0, suppressedCalls);
    }

    [Fact]
    public void Reset_RemovesTrackedState_SoNextCallIsTreatedAsFirst()
    {
        var throttler = new ThrottledLogger();
        throttler.ShouldLog("key", TimeSpan.FromDays(1), out _); // allowed
        throttler.ShouldLog("key", TimeSpan.FromDays(1), out _); // suppressed

        throttler.Reset("key");

        var result = throttler.ShouldLog("key", TimeSpan.FromDays(1), out var suppressed);

        Assert.True(result);
        Assert.Equal(0, suppressed);
    }

    [Fact]
    public void TryGetSuppressedCount_UntrackedKey_ReturnsFalse()
    {
        var throttler = new ThrottledLogger();

        var found = throttler.TryGetSuppressedCount("key", out var suppressed);

        Assert.False(found);
        Assert.Equal(0, suppressed);
    }

    [Fact]
    public void TryGetSuppressedCount_TrackedKey_ReturnsCurrentSuppressedCount()
    {
        var throttler = new ThrottledLogger();
        throttler.ShouldLog("key", TimeSpan.FromDays(1), out _); // allowed
        throttler.ShouldLog("key", TimeSpan.FromDays(1), out _); // suppressed (count=1)
        throttler.ShouldLog("key", TimeSpan.FromDays(1), out _); // suppressed (count=2)

        var found = throttler.TryGetSuppressedCount("key", out var suppressed);

        Assert.True(found);
        Assert.Equal(2, suppressed);
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
