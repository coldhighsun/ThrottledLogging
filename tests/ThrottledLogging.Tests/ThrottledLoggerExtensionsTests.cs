using System.Globalization;
using Microsoft.Extensions.Logging;
using ThrottledLogging.Resources;
using Xunit;

namespace ThrottledLogging.Tests;

/// <summary>
/// Tests for <see cref="ThrottledLoggerExtensions"/>.
/// </summary>
[Collection("Sequential")]
public class ThrottledLoggerExtensionsTests
{
    /// <summary>
    /// Verifies that the first throttled call is logged at its level with the formatted message.
    /// </summary>
    [Fact]
    public void LogInformationThrottled_FirstCall_LogsMessage()
    {
        var logger = new FakeLogger();

        logger.LogInformationThrottled("key", TimeSpan.FromMinutes(1), "Hello {Name}", "world");

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, logger.Entries[0].Level);
        Assert.Contains("world", logger.Entries[0].Message);
    }

    /// <summary>
    /// Verifies that a repeated call within the interval is not logged.
    /// </summary>
    [Fact]
    public void LogInformationThrottled_WithinInterval_DoesNotLog()
    {
        var logger = new FakeLogger();
        logger.LogInformationThrottled("key", TimeSpan.FromDays(1), "Msg");

        logger.LogInformationThrottled("key", TimeSpan.FromDays(1), "Msg");

        Assert.Single(logger.Entries);
    }

    /// <summary>
    /// Verifies that the message logged after suppression reports how many calls were suppressed.
    /// </summary>
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

    /// <summary>
    /// Verifies that the suppressed-count suffix follows the current culture rather than the culture first cached for a template.
    /// </summary>
    [Fact]
    public void LogThrottled_AfterSuppression_UsesCurrentCultureNotFirstCachedCulture()
    {
        try
        {
            var interval = TimeSpan.FromMilliseconds(1000);

            Messages.Culture = CultureInfo.InvariantCulture;
            var enLogger = new FakeLogger();
            enLogger.LogInformationThrottled("en-key", interval, "Msg"); // logged
            enLogger.LogInformationThrottled("en-key", interval, "Msg"); // suppressed (1)
            Thread.Sleep(1500);
            enLogger.LogInformationThrottled("en-key", interval, "Msg"); // logged with count, caches en suffix for "Msg"

            Messages.Culture = new CultureInfo("zh-CN");
            var zhLogger = new FakeLogger();
            zhLogger.LogInformationThrottled("zh-key", interval, "Msg"); // logged
            zhLogger.LogInformationThrottled("zh-key", interval, "Msg"); // suppressed (1)
            Thread.Sleep(1500);
            zhLogger.LogInformationThrottled("zh-key", interval, "Msg"); // logged with count, must use zh suffix

            Assert.Contains("messages suppressed", enLogger.Entries[1].Message);
            Assert.Contains("个消息被隐藏", zhLogger.Entries[1].Message);
        }
        finally
        {
            Messages.Culture = null;
        }
    }

    /// <summary>
    /// Verifies that each level-specific method logs at its own level.
    /// </summary>
    /// <param name="level">The level to log at.</param>
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

    /// <summary>
    /// Verifies that the same key is throttled independently on different loggers.
    /// </summary>
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

    /// <summary>
    /// Verifies that a call filtered out by the log level does not start a throttle window.
    /// </summary>
    [Fact]
    public void LogThrottled_DisabledLogLevel_DoesNotConsumeThrottleQuota()
    {
        // Calls that are filtered by IsEnabled should not advance the throttle state,
        // so re-enabling the level should allow the next call through without suppression.
        var logger = new FakeLogger { MinLevel = LogLevel.Warning };

        logger.LogInformationThrottled("key", TimeSpan.FromDays(1), "Msg"); // filtered, quota untouched

        logger.MinLevel = LogLevel.Trace;
        logger.LogInformationThrottled("key", TimeSpan.FromDays(1), "Msg"); // should be allowed (first real call)

        Assert.Single(logger.Entries);
    }

    /// <summary>
    /// Verifies that a call at a disabled level is not logged.
    /// </summary>
    [Fact]
    public void LogThrottled_DisabledLogLevel_DoesNotLog()
    {
        var logger = new FakeLogger { MinLevel = LogLevel.Warning };

        logger.LogInformationThrottled("key", TimeSpan.Zero, "Msg");

        Assert.Empty(logger.Entries);
    }

    /// <summary>
    /// Verifies that a <see langword="null"/> template still gets the suppressed count appended after suppression.
    /// </summary>
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

    /// <summary>
    /// Verifies that with a <see langword="null"/> template and extra args, the resumed message renders like the
    /// first one ("[null]") and the suffix placeholder binds to the suppressed count rather than to the first arg.
    /// </summary>
    [Fact]
    public void LogThrottled_NullTemplateWithArgs_SuffixBindsToSuppressedCount()
    {
        Messages.Culture = CultureInfo.InvariantCulture;
        try
        {
            var logger = new FakeLogger();

            logger.LogInformationThrottled("key", TimeSpan.FromDays(1), null, 42);
            logger.LogInformationThrottled("key", TimeSpan.FromDays(1), null, 42); // suppressed (1)
            logger.LogInformationThrottled("key", TimeSpan.Zero, null, 42);

            Assert.Equal("[null]", logger.Entries[0].Message);
            Assert.Equal("[null] (1 messages suppressed)", logger.Entries[^1].Message);
        }
        finally
        {
            Messages.Culture = null;
        }
    }

    /// <summary>
    /// Verifies that a template logged without args, which is output verbatim on first use, is still output
    /// verbatim once the suppressed count is appended, instead of having its braces parsed as placeholders.
    /// </summary>
    /// <param name="messageTemplate">The message template to log.</param>
    [Theory]
    [InlineData("Unexpected token '{' in input")]
    [InlineData("Bad payload {\"a\":1}")]
    [InlineData("Got {{literal}}")]
    [InlineData("Value {Missing}")]
    [InlineData("Stray } brace")]
    public void LogThrottled_NoArgsTemplateWithBraces_ResumedMessageRendersTemplateVerbatim(string messageTemplate)
    {
        Messages.Culture = CultureInfo.InvariantCulture;
        try
        {
            var logger = new FakeLogger();

            logger.LogInformationThrottled("key", TimeSpan.FromDays(1), messageTemplate);
            logger.LogInformationThrottled("key", TimeSpan.FromDays(1), messageTemplate); // suppressed (1)
            logger.LogInformationThrottled("key", TimeSpan.Zero, messageTemplate);

            Assert.Equal(messageTemplate, logger.Entries[0].Message);
            Assert.Equal($"{messageTemplate} (1 messages suppressed)", logger.Entries[^1].Message);
        }
        finally
        {
            Messages.Culture = null;
        }
    }

    /// <summary>
    /// Verifies that arguments beyond the template's placeholders are ignored, so the suffix placeholder binds to the
    /// suppressed count rather than to an extra argument.
    /// </summary>
    /// <param name="messageTemplate">The message template to log with the args <c>91, 42</c>.</param>
    /// <param name="expected">The expected rendering of the first message.</param>
    [Theory]
    [InlineData("Disk full", "Disk full")]
    [InlineData("Disk {Percent}% full", "Disk 91% full")]
    public void LogThrottled_MoreArgsThanPlaceholders_SuffixBindsToSuppressedCount(string messageTemplate, string expected)
    {
        Messages.Culture = CultureInfo.InvariantCulture;
        try
        {
            var logger = new FakeLogger();

            logger.LogInformationThrottled("key", TimeSpan.FromDays(1), messageTemplate, 91, 42);
            logger.LogInformationThrottled("key", TimeSpan.FromDays(1), messageTemplate, 91, 42); // suppressed (1)
            logger.LogInformationThrottled("key", TimeSpan.Zero, messageTemplate, 91, 42);

            Assert.Equal(expected, logger.Entries[0].Message);
            Assert.Equal($"{expected} (1 messages suppressed)", logger.Entries[^1].Message);
        }
        finally
        {
            Messages.Culture = null;
        }
    }

    /// <summary>
    /// Verifies that with fewer arguments than placeholders, the resumed message fails to render just like the first
    /// one does, instead of the suppressed count silently filling the missing placeholder.
    /// </summary>
    [Fact]
    public void LogThrottled_FewerArgsThanPlaceholders_ResumedMessageFailsLikeFirst()
    {
        var logger = new FakeLogger();

        Assert.Throws<FormatException>(
            () => logger.LogInformationThrottled("key", TimeSpan.FromDays(1), "{First} and {Second}", 1));
        logger.LogInformationThrottled("key", TimeSpan.FromDays(1), "{First} and {Second}", 1); // suppressed (1)

        Assert.Throws<FormatException>(
            () => logger.LogInformationThrottled("key", TimeSpan.Zero, "{First} and {Second}", 1));
    }

    /// <summary>
    /// Verifies that a formatted template with escaped braces keeps rendering them the same way once the suppressed count is appended.
    /// </summary>
    [Fact]
    public void LogThrottled_FormattedTemplateWithEscapedBraces_ResumedMessageRendersLikeFirst()
    {
        Messages.Culture = CultureInfo.InvariantCulture;
        try
        {
            var logger = new FakeLogger();

            logger.LogInformationThrottled("key", TimeSpan.FromDays(1), "{{Literal}} {Value}", 5);
            logger.LogInformationThrottled("key", TimeSpan.FromDays(1), "{{Literal}} {Value}", 5); // suppressed (1)
            logger.LogInformationThrottled("key", TimeSpan.Zero, "{{Literal}} {Value}", 5);

            Assert.Equal("{Literal} 5", logger.Entries[0].Message);
            Assert.Equal("{Literal} 5 (1 messages suppressed)", logger.Entries[^1].Message);
        }
        finally
        {
            Messages.Culture = null;
        }
    }

    /// <summary>
    /// Verifies that the resumed message keeps the original structured values, adds the suppressed count, and reports
    /// the original template followed by the suffix as <c>{OriginalFormat}</c>.
    /// </summary>
    [Fact]
    public void LogThrottled_AfterSuppression_StateHasOriginalValuesSuppressedCountAndOriginalFormat()
    {
        Messages.Culture = CultureInfo.InvariantCulture;
        try
        {
            var logger = new FakeLogger();

            logger.LogInformationThrottled("key", TimeSpan.FromDays(1), "Disk {Percent}% full", 91);
            logger.LogInformationThrottled("key", TimeSpan.FromDays(1), "Disk {Percent}% full", 91); // suppressed (1)
            logger.LogInformationThrottled("key", TimeSpan.Zero, "Disk {Percent}% full", 91);

            KeyValuePair<string, object?>[] expected =
            [
                new("Percent", 91),
                new("SuppressedCount", 1),
                new("{OriginalFormat}", "Disk {Percent}% full ({SuppressedCount} messages suppressed)"),
            ];
            Assert.Equal(expected, logger.Entries[^1].State);
        }
        finally
        {
            Messages.Culture = null;
        }
    }

    /// <summary>
    /// Verifies that a template logged without args keeps its braces unescaped in <c>{OriginalFormat}</c> once resumed.
    /// </summary>
    [Fact]
    public void LogThrottled_NoArgsTemplateWithBraces_OriginalFormatIsNotEscaped()
    {
        Messages.Culture = CultureInfo.InvariantCulture;
        try
        {
            var logger = new FakeLogger();

            logger.LogInformationThrottled("key", TimeSpan.FromDays(1), "Bad payload {\"a\":1}");
            logger.LogInformationThrottled("key", TimeSpan.FromDays(1), "Bad payload {\"a\":1}"); // suppressed (1)
            logger.LogInformationThrottled("key", TimeSpan.Zero, "Bad payload {\"a\":1}");

            Assert.Contains(
                KeyValuePair.Create<string, object?>("{OriginalFormat}", "Bad payload {\"a\":1} ({SuppressedCount} messages suppressed)"),
                logger.Entries[^1].State!);
        }
        finally
        {
            Messages.Culture = null;
        }
    }

    /// <summary>
    /// Verifies that a template that already uses <c>{SuppressedCount}</c> keeps its own value, while the suppressed
    /// count is published under a distinct name.
    /// </summary>
    [Fact]
    public void LogThrottled_TemplateUsesSuppressedCount_SuppressedCountGetsDistinctName()
    {
        Messages.Culture = CultureInfo.InvariantCulture;
        try
        {
            var logger = new FakeLogger();

            logger.LogWarningThrottled("key", TimeSpan.FromDays(1), "Retry {SuppressedCount}", 5);
            logger.LogWarningThrottled("key", TimeSpan.FromDays(1), "Retry {SuppressedCount}", 5); // suppressed (1)
            logger.LogWarningThrottled("key", TimeSpan.Zero, "Retry {SuppressedCount}", 5);

            KeyValuePair<string, object?>[] expected =
            [
                new("SuppressedCount", 5),
                new("SuppressedCount_1", 1),
                new("{OriginalFormat}", "Retry {SuppressedCount} ({SuppressedCount_1} messages suppressed)"),
            ];
            Assert.Equal("Retry 5 (1 messages suppressed)", logger.Entries[^1].Message);
            Assert.Equal(expected, logger.Entries[^1].State);
        }
        finally
        {
            Messages.Culture = null;
        }
    }

    /// <summary>
    /// Verifies that the overload taking an exception logs the formatted message at the error level.
    /// </summary>
    [Fact]
    public void LogErrorThrottled_WithException_LogsMessage()
    {
        var logger = new FakeLogger();
        var exception = new InvalidOperationException("boom");

        logger.LogErrorThrottled("key", TimeSpan.FromMinutes(1), exception, "Failed {Name}", "world");

        Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, logger.Entries[0].Level);
        Assert.Contains("world", logger.Entries[0].Message);
    }

    /// <summary>
    /// Verifies that resetting a key lets the next call be logged immediately.
    /// </summary>
    [Fact]
    public void ResetThrottle_AllowsImmediateReLog()
    {
        var logger = new FakeLogger();
        var interval = TimeSpan.FromDays(1);

        logger.LogInformationThrottled("key", interval, "Msg"); // logged
        logger.LogInformationThrottled("key", interval, "Msg"); // suppressed

        logger.ResetThrottle("key");
        logger.LogInformationThrottled("key", interval, "Msg"); // treated as first again

        Assert.Equal(2, logger.Entries.Count);
    }

    /// <summary>
    /// Verifies that the suppressed count of a key can be queried without logging.
    /// </summary>
    [Fact]
    public void TryGetThrottledSuppressedCount_TracksSuppressedCalls()
    {
        var logger = new FakeLogger();
        var interval = TimeSpan.FromDays(1);

        logger.LogInformationThrottled("key", interval, "Msg"); // logged
        logger.LogInformationThrottled("key", interval, "Msg"); // suppressed (1)
        logger.LogInformationThrottled("key", interval, "Msg"); // suppressed (2)

        var found = logger.TryGetThrottledSuppressedCount("key", out var suppressed);

        Assert.True(found);
        Assert.Equal(2, suppressed);
    }

    /// <summary>
    /// Verifies that querying a logger that has never logged throttled reports the key as untracked, without creating
    /// a throttler for the logger.
    /// </summary>
    [Fact]
    public void TryGetThrottledSuppressedCount_LoggerNeverThrottled_ReturnsFalseWithoutCreatingThrottler()
    {
        var logger = new FakeLogger();

        var found = logger.TryGetThrottledSuppressedCount("key", out var suppressed);

        Assert.False(found);
        Assert.Equal(0, suppressed);
        Assert.False(ThrottledLogger.TryGet(logger, out _));
    }

    /// <summary>
    /// Verifies that resetting a key on a logger that has never logged throttled does not create a throttler for it.
    /// </summary>
    [Fact]
    public void ResetThrottle_LoggerNeverThrottled_DoesNotCreateThrottler()
    {
        var logger = new FakeLogger();

        logger.ResetThrottle("key");

        Assert.False(ThrottledLogger.TryGet(logger, out _));
    }

    /// <summary>
    /// Verifies that logging with a <see langword="null"/> key throws <see cref="ArgumentNullException"/>, whether or
    /// not the level is enabled.
    /// </summary>
    /// <param name="minLevel">The minimum level enabled on the logger.</param>
    [Theory]
    [InlineData(LogLevel.Trace)]
    [InlineData(LogLevel.None)]
    public void LogWarningThrottled_NullKey_ThrowsArgumentNullException(LogLevel minLevel)
    {
        var logger = new FakeLogger { MinLevel = minLevel };

        var exception = Assert.Throws<ArgumentNullException>(
            () => logger.LogWarningThrottled(null!, TimeSpan.FromMinutes(1), "Msg"));

        Assert.Equal("key", exception.ParamName);
    }

    /// <summary>
    /// Verifies that resetting a <see langword="null"/> key throws <see cref="ArgumentNullException"/>, even on a
    /// logger that has never logged throttled.
    /// </summary>
    [Fact]
    public void ResetThrottle_NullKey_ThrowsArgumentNullException()
    {
        var logger = new FakeLogger();

        var exception = Assert.Throws<ArgumentNullException>(() => logger.ResetThrottle(null!));

        Assert.Equal("key", exception.ParamName);
    }

    /// <summary>
    /// Verifies that querying a <see langword="null"/> key throws <see cref="ArgumentNullException"/>, even on a
    /// logger that has never logged throttled.
    /// </summary>
    [Fact]
    public void TryGetThrottledSuppressedCount_NullKey_ThrowsArgumentNullException()
    {
        var logger = new FakeLogger();

        var exception = Assert.Throws<ArgumentNullException>(
            () => logger.TryGetThrottledSuppressedCount(null!, out _));

        Assert.Equal("key", exception.ParamName);
    }

    /// <summary>
    /// Verifies that logging through a <see langword="null"/> logger throws <see cref="ArgumentNullException"/>.
    /// </summary>
    [Fact]
    public void LogWarningThrottled_NullLogger_ThrowsArgumentNullException()
    {
        ILogger logger = null!;

        var exception = Assert.Throws<ArgumentNullException>(
            () => logger.LogWarningThrottled("key", TimeSpan.FromMinutes(1), "Msg"));

        Assert.Equal("logger", exception.ParamName);
    }

    /// <summary>
    /// Verifies that resetting a key on a <see langword="null"/> logger throws <see cref="ArgumentNullException"/>.
    /// </summary>
    [Fact]
    public void ResetThrottle_NullLogger_ThrowsArgumentNullException()
    {
        ILogger logger = null!;

        var exception = Assert.Throws<ArgumentNullException>(() => logger.ResetThrottle("key"));

        Assert.Equal("logger", exception.ParamName);
    }

    /// <summary>
    /// Verifies that querying a <see langword="null"/> logger throws <see cref="ArgumentNullException"/>.
    /// </summary>
    [Fact]
    public void TryGetThrottledSuppressedCount_NullLogger_ThrowsArgumentNullException()
    {
        ILogger logger = null!;

        var exception = Assert.Throws<ArgumentNullException>(
            () => logger.TryGetThrottledSuppressedCount("key", out _));

        Assert.Equal("logger", exception.ParamName);
    }
}