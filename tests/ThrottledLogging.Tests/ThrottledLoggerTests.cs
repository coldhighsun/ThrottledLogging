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
    public void ShouldLog_ZeroInterval_AlwaysReturnsTrue()
    {
        var throttler = new ThrottledLogger();

        throttler.ShouldLog("key", TimeSpan.Zero, out _);
        var result = throttler.ShouldLog("key", TimeSpan.Zero, out var suppressed);

        Assert.True(result);
        Assert.Equal(0, suppressed);
    }
}