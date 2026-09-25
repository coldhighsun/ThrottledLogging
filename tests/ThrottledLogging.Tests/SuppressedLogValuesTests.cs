using System.Globalization;
using ThrottledLogging.Resources;
using Xunit;

namespace ThrottledLogging.Tests;

/// <summary>
/// Tests for <see cref="SuppressedLogValues"/>.
/// </summary>
[Collection("Sequential")]
public class SuppressedLogValuesTests
{
    /// <summary>
    /// Verifies that the indexer exposes the original values, then the suppressed count, then the suffixed template,
    /// and that enumeration yields the same values in the same order.
    /// </summary>
    [Fact]
    public void Indexer_FormattedTemplate_MatchesEnumerationOrder()
    {
        Messages.Culture = CultureInfo.InvariantCulture;
        try
        {
            var state = SuppressedLogValues.Create("Disk {Percent}% full", [91], 3);

            KeyValuePair<string, object?>[] expected =
            [
                new("Percent", 91),
                new("SuppressedCount", 3),
                new("{OriginalFormat}", "Disk {Percent}% full ({SuppressedCount} messages suppressed)"),
            ];
            Assert.Equal(expected.Length, state.Count);
            Assert.Equal(expected, Enumerable.Range(0, state.Count).Select(i => state[i]));
            Assert.Equal(expected, state);
        }
        finally
        {
            Messages.Culture = null;
        }
    }

    /// <summary>
    /// Verifies that an index outside the values is rejected.
    /// </summary>
    /// <param name="index">The out-of-range index.</param>
    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void Indexer_OutOfRange_ThrowsArgumentOutOfRangeException(int index)
    {
        var state = SuppressedLogValues.Create("Disk {Percent}% full", [91], 3);

        Assert.Throws<ArgumentOutOfRangeException>(() => state[index]);
    }

    /// <summary>
    /// Verifies that creating the state for fewer arguments than placeholders does not read the missing values, so
    /// that, as with the original state, only rendering or enumerating it fails.
    /// </summary>
    [Fact]
    public void Create_FewerArgsThanPlaceholders_DefersFailureUntilAccess()
    {
        var state = SuppressedLogValues.Create("{First} and {Second}", [1], 3);

        Assert.Equal(new KeyValuePair<string, object?>("SuppressedCount", 3), state[2]);
        Assert.Throws<FormatException>(state.ToString);
    }

    /// <summary>
    /// Verifies that the suffix is rendered with the same template rules as the <c>{OriginalFormat}</c> it is published
    /// in, so format specifiers and escaped braces in a translation are honored.
    /// </summary>
    /// <param name="suffix">The suffix template.</param>
    /// <param name="expected">The expected rendered message.</param>
    [Theory]
    [InlineData(" ({SuppressedCount:N0} suppressed)", "Msg (1,234 suppressed)")]
    [InlineData(" {{{SuppressedCount}}}", "Msg {1234}")]
    public void ToString_SuffixWithTemplateSyntax_RendersSuffixAsTemplate(string suffix, string expected)
    {
        var state = SuppressedLogValues.Create("Msg", [], 1234, suffix);

        var message = state.ToString();

        Assert.Equal(expected, message);
        Assert.Equal(new KeyValuePair<string, object?>("{OriginalFormat}", "Msg" + suffix), state[^1]);
    }

    /// <summary>
    /// Verifies that when the original message already has a <c>SuppressedCount</c> value, the suppressed count gets
    /// a distinct name in both the structured values and the suffix, so name-binding sinks do not confuse the two.
    /// </summary>
    [Fact]
    public void Create_OriginalValueNamedSuppressedCount_RenamesSuppressedCountValue()
    {
        var state = SuppressedLogValues.Create("Retry {SuppressedCount} {SuppressedCount_1}", [5, 6], 3, " ({SuppressedCount} messages suppressed)");

        KeyValuePair<string, object?>[] expected =
        [
            new("SuppressedCount", 5),
            new("SuppressedCount_1", 6),
            new("SuppressedCount_2", 3),
            new("{OriginalFormat}", "Retry {SuppressedCount} {SuppressedCount_1} ({SuppressedCount_2} messages suppressed)"),
        ];
        Assert.Equal(expected, state);
        Assert.Equal("Retry 5 6 (3 messages suppressed)", state.ToString());
    }

    /// <summary>
    /// Verifies that Simplified Chinese cultures other than zh-CN also fall back to the Chinese suffix.
    /// </summary>
    /// <param name="cultureName">The name of the culture to render the suffix in.</param>
    [Theory]
    [InlineData("zh-CN")]
    [InlineData("zh-Hans")]
    [InlineData("zh-SG")]
    public void ToString_SimplifiedChineseCulture_UsesChineseSuffix(string cultureName)
    {
        Messages.Culture = new CultureInfo(cultureName);
        try
        {
            var state = SuppressedLogValues.Create("Msg", [], 3);

            Assert.Equal("Msg (3个消息被隐藏)", state.ToString());
        }
        finally
        {
            Messages.Culture = null;
        }
    }

    /// <summary>
    /// Verifies that dynamically built templates cannot grow the template cache beyond its limit, and that templates
    /// no longer cached still get the correct <c>{OriginalFormat}</c>.
    /// </summary>
    [Fact]
    public void Create_MoreTemplatesThanCacheLimit_CacheStaysBoundedAndOriginalFormatIsCorrect()
    {
        const string suffix = " ({SuppressedCount} dropped)";
        var prefix = Guid.NewGuid().ToString("N");
        SuppressedLogValues? last = null;

        for (var i = 0; i < SuppressedLogValues.MaxCachedOriginalFormats + 10; i++)
        {
            last = SuppressedLogValues.Create($"{prefix} {i}", [], 1, suffix);
        }

        Assert.True(SuppressedLogValues.CachedOriginalFormatCount <= SuppressedLogValues.MaxCachedOriginalFormats);
        Assert.Equal(
            KeyValuePair.Create<string, object?>(
                "{OriginalFormat}",
                $"{prefix} {SuppressedLogValues.MaxCachedOriginalFormats + 9}{suffix}"),
            last![^1]);
    }
}
