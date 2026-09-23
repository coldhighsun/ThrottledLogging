using Xunit;

namespace ThrottledLogging.Tests;

[Collection("Sequential")]
public class ThrottledLoggerTests
{
    [Fact]
    public void Configure_ShorterExpiry_CausesCleanupToRemoveEntries()
    {
        ThrottledLogger.Configure(expiry: TimeSpan.FromMilliseconds(1), cleanupPeriod: TimeSpan.FromMilliseconds(50));
        try
        {
            var logger = new FakeLogger();
            logger.LogInformationThrottled("key", TimeSpan.FromDays(1), "Msg"); // entry created, throttled

            Thread.Sleep(200); // wait for cleanup to run and expire the entry

            // After expiry the entry is gone, so the next call should be allowed as a fresh first call
            logger.LogInformationThrottled("key", TimeSpan.FromDays(1), "Msg");

            Assert.Equal(2, logger.Entries.Count);
        }
        finally
        {
            ThrottledLogger.Configure(expiry: TimeSpan.FromHours(1), cleanupPeriod: TimeSpan.FromHours(1));
        }
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
}