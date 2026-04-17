using System.Globalization;
using Microsoft.Extensions.Logging;
using ThrottledLogging.Resources;
using Xunit;

namespace ThrottledLogging.Tests;

public class ThrottledLoggerExtensionsTests
{
    [Fact]
    public void LogInformationThrottled_FirstCall_LogsMessage()
    {
        var logger = new FakeLogger();

        logger.LogInformationThrottled("key", TimeSpan.FromMinutes(1), "Hello {Name}", "world");

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, logger.Entries[0].Level);
        Assert.Contains("world", logger.Entries[0].Message);
    }

    [Fact]
    public void LogInformationThrottled_WithinInterval_DoesNotLog()
    {
        var logger = new FakeLogger();
        logger.LogInformationThrottled("key", TimeSpan.FromDays(1), "Msg");

        logger.LogInformationThrottled("key", TimeSpan.FromDays(1), "Msg");

        Assert.Single(logger.Entries);
    }

    [Fact]
    public void LogThrottled_AfterSuppression_AppendsSuppressedCount()
    {
        Messages.Culture = CultureInfo.InvariantCulture;
        try
        {
            var logger = new FakeLogger();
            var interval = TimeSpan.FromMilliseconds(1000);

            logger.LogInformationThrottled("key", interval, "Msg"); // logged
            logger.LogInformationThrottled("key", interval, "Msg"); // suppressed (1)
            logger.LogInformationThrottled("key", interval, "Msg"); // suppressed (2)

            Thread.Sleep(2000);
            logger.LogInformationThrottled("key", interval, "Msg"); // logged with count

            Assert.Equal(2, logger.Entries.Count);
            Assert.Contains("2 messages suppressed", logger.Entries[1].Message);
        }
        finally
        {
            Messages.Culture = null;
        }
    }

    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Information)]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Error)]
    [InlineData(LogLevel.Critical)]
    public void LogThrottled_AllLogLevels_EmitCorrectLevel(LogLevel level)
    {
        var logger = new FakeLogger();

        switch (level)
        {
            case LogLevel.Trace:
                logger.LogTraceThrottled("k", TimeSpan.Zero, "m");
                break;

            case LogLevel.Debug:
                logger.LogDebugThrottled("k", TimeSpan.Zero, "m");
                break;

            case LogLevel.Information:
                logger.LogInformationThrottled("k", TimeSpan.Zero, "m");
                break;

            case LogLevel.Warning:
                logger.LogWarningThrottled("k", TimeSpan.Zero, "m");
                break;

            case LogLevel.Error:
                logger.LogErrorThrottled("k", TimeSpan.Zero, "m");
                break;

            case LogLevel.Critical:
                logger.LogCriticalThrottled("k", TimeSpan.Zero, "m");
                break;
        }

        Assert.Single(logger.Entries);
        Assert.Equal(level, logger.Entries[0].Level);
    }

    [Fact]
    public void LogThrottled_DifferentLoggers_ThrottledIndependently()
    {
        var logger1 = new FakeLogger();
        var logger2 = new FakeLogger();
        var interval = TimeSpan.FromDays(1);

        logger1.LogInformationThrottled("key", interval, "From 1");
        logger2.LogInformationThrottled("key", interval, "From 2");

        Assert.Single(logger1.Entries);
        Assert.Single(logger2.Entries);
    }

    [Fact]
    public void LogThrottled_DisabledLogLevel_DoesNotLog()
    {
        var logger = new FakeLogger { MinLevel = LogLevel.Warning };

        logger.LogInformationThrottled("key", TimeSpan.Zero, "Msg");

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void LogThrottled_NullTemplate_AppendsSuppressedCount()
    {
        Messages.Culture = CultureInfo.InvariantCulture;
        try
        {
            var logger = new FakeLogger();
            var interval = TimeSpan.FromMilliseconds(1000);

            logger.LogInformationThrottled("key", interval, null);
            logger.LogInformationThrottled("key", interval, null); // suppressed (1)

            Thread.Sleep(2000);
            logger.LogInformationThrottled("key", interval, null);

            Assert.Equal(2, logger.Entries.Count);
            Assert.Contains("1 messages suppressed", logger.Entries[1].Message);
        }
        finally
        {
            Messages.Culture = null;
        }
    }
}
