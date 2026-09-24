using Microsoft.Extensions.Logging;
using System.Collections;
using System.Collections.Concurrent;
using ThrottledLogging.Resources;

namespace ThrottledLogging;

/// <summary>
/// The structured log state of a message that resumes after suppression: the state of the original message, extended
/// with the suppressed count, and rendered as the original message followed by the localized suppressed-count suffix.
/// </summary>
/// <remarks>
/// Both the original message and the suffix are rendered by <see cref="LoggerExtensions"/> itself instead of by rewriting
/// or parsing templates, so the original part renders exactly as it would without suppression, regardless of template
/// syntax (literal or escaped braces, extra or missing arguments, a <see langword="null"/> template) or the logging
/// library version, and the suffix follows the same template rules as the <c>{OriginalFormat}</c> it is published in.
/// </remarks>
internal sealed class SuppressedLogValues : IReadOnlyList<KeyValuePair<string, object?>>
{
    /// <summary>
    /// The name of the structured value that holds the suppressed count, unless the original message already uses it.
    /// </summary>
    private const string SuppressedCountName = "SuppressedCount";

    /// <summary>
    /// The start of the suppressed count placeholder in the localized suffix, before any alignment or format.
    /// </summary>
    private const string SuppressedCountPlaceholderStart = "{" + SuppressedCountName;

    /// <summary>
    /// The name of the structured value that holds the message template, by <see cref="ILogger"/> convention.
    /// </summary>
    private const string OriginalFormatName = "{OriginalFormat}";

    /// <summary>
    /// Caches the original message template concatenated with the suffix, to avoid repeated string allocations for
    /// the same template. Keyed by both the template and the suffix, since the suffix is localized.
    /// </summary>
    private static readonly ConcurrentDictionary<(string OriginalFormat, string Suffix), string> OriginalFormatCache = new();

    /// <summary>
    /// The structured state of the original message. Read lazily, like the original state itself would be, because
    /// it may fail on access (for example when there are fewer arguments than placeholders).
    /// </summary>
    private readonly IReadOnlyList<KeyValuePair<string, object?>> _originalState;

    /// <summary>
    /// The number of structured values of the original message, excluding its trailing <c>{OriginalFormat}</c> value.
    /// </summary>
    private readonly int _originalValueCount;

    /// <summary>
    /// The original message template followed by the suffix.
    /// </summary>
    private readonly string _originalFormat;

    /// <summary>
    /// Renders the original message, using the formatter supplied with the original state.
    /// </summary>
    private readonly Func<string> _renderOriginal;

    /// <summary>
    /// Renders the suffix with the suppressed count filled in, using the formatter supplied with the suffix state.
    /// </summary>
    private readonly Func<string> _renderSuffix;

    /// <summary>
    /// The name of the structured value that holds the suppressed count, as it appears in the suffix.
    /// </summary>
    private readonly string _suppressedCountName;

    /// <summary>
    /// The number of log calls suppressed since the previous successful log.
    /// </summary>
    private readonly int _suppressedCount;

    /// <summary>
    /// The rendered message, cached after the first call to <see cref="ToString"/>.
    /// </summary>
    private string? _cachedToString;

    /// <summary>
    /// Initializes a new instance of the <see cref="SuppressedLogValues"/> class.
    /// </summary>
    /// <param name="originalState">The structured state of the original message.</param>
    /// <param name="renderOriginal">Renders the original message.</param>
    /// <param name="suffix">The suffix template, with the suppressed count placeholder named <paramref name="suppressedCountName"/>.</param>
    /// <param name="renderSuffix">Renders the suffix with the suppressed count filled in.</param>
    /// <param name="suppressedCountName">The name of the structured value that holds the suppressed count.</param>
    /// <param name="suppressedCount">The number of log calls suppressed since the previous successful log.</param>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="originalState"/> does not end with a <c>{OriginalFormat}</c> value.
    /// </exception>
    private SuppressedLogValues(
        IReadOnlyList<KeyValuePair<string, object?>> originalState,
        Func<string> renderOriginal,
        string suffix,
        Func<string> renderSuffix,
        string suppressedCountName,
        int suppressedCount)
    {
        var originalCount = originalState.Count;

        // By ILogger convention the template is the last value; only that one is read eagerly, which is always safe.
        var last = originalCount > 0 ? originalState[originalCount - 1] : default;
        if (last.Key != OriginalFormatName || last.Value is not string originalFormat)
        {
            throw new InvalidOperationException($"The logging library produced a message state without a trailing {OriginalFormatName} value.");
        }

        _originalState = originalState;
        _originalValueCount = originalCount - 1;
        _originalFormat = OriginalFormatCache.GetOrAdd(
            (originalFormat, suffix),
            static key => string.Concat(key.OriginalFormat, key.Suffix));
        _renderOriginal = renderOriginal;
        _renderSuffix = renderSuffix;
        _suppressedCountName = suppressedCountName;
        _suppressedCount = suppressedCount;
    }

    /// <summary>
    /// Gets the formatter that renders a <see cref="SuppressedLogValues"/> for <see cref="ILogger.Log{TState}"/>.
    /// </summary>
    public static Func<SuppressedLogValues, Exception?, string> Formatter
    {
        get;
    } = static (state, _) => state.ToString();

    /// <summary>
    /// Gets the number of structured values: those of the original message, the suppressed count, and the template.
    /// </summary>
    public int Count => _originalValueCount + 2;

    /// <summary>
    /// Gets the structured value at the specified index: the original message's values in order, then the suppressed
    /// count, then the original template followed by the suffix.
    /// </summary>
    /// <param name="index">The zero-based index of the value to get.</param>
    /// <returns>The structured value at <paramref name="index"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative or not less than <see cref="Count"/>.</exception>
    public KeyValuePair<string, object?> this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);

            if (index < _originalValueCount)
            {
                return _originalState[index];
            }

            return index == _originalValueCount
                ? new(_suppressedCountName, _suppressedCount)
                : new(OriginalFormatName, _originalFormat);
        }
    }

    /// <summary>
    /// Creates the state of a message that resumes after suppression, using the suppressed-count suffix of the current culture.
    /// </summary>
    /// <param name="messageTemplate">The original message template.</param>
    /// <param name="args">The original message template arguments.</param>
    /// <param name="suppressedCount">The number of log calls suppressed since the previous successful log.</param>
    /// <returns>The state to log.</returns>
    public static SuppressedLogValues Create(string? messageTemplate, object?[] args, int suppressedCount)
        => Create(messageTemplate, args, suppressedCount, Messages.SuppressedSuffix);

    /// <summary>
    /// Creates the state of a message that resumes after suppression, using the specified suffix.
    /// </summary>
    /// <param name="messageTemplate">The original message template.</param>
    /// <param name="args">The original message template arguments.</param>
    /// <param name="suppressedCount">The number of log calls suppressed since the previous successful log.</param>
    /// <param name="suffix">The suffix template, containing a <c>{SuppressedCount}</c> placeholder.</param>
    /// <returns>The state to log.</returns>
    internal static SuppressedLogValues Create(string? messageTemplate, object?[] args, int suppressedCount, string suffix)
    {
        var capture = new StateCapture();

        // Lets the standard extension build the original state exactly as it would for an unsuppressed call.
        var (originalState, renderOriginal) = capture.Capture(messageTemplate, args);

        var suppressedCountName = GetSuppressedCountName(originalState, Math.Min(args.Length, originalState.Count - 1));
        if (suppressedCountName != SuppressedCountName)
        {
            // The suffix is our own resource, so its only use of this text is the suppressed count placeholder.
            suffix = suffix.Replace(SuppressedCountPlaceholderStart, "{" + suppressedCountName);
        }

        var (_, renderSuffix) = capture.Capture(suffix, [suppressedCount]);

        return new(originalState, renderOriginal, suffix, renderSuffix, suppressedCountName, suppressedCount);
    }

    /// <summary>
    /// Returns an enumerator that iterates through the structured values.
    /// </summary>
    /// <returns>An enumerator for the structured values.</returns>
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
    {
        for (var i = 0; i < Count; i++)
        {
            yield return this[i];
        }
    }

    /// <summary>
    /// Returns an enumerator that iterates through the structured values.
    /// </summary>
    /// <returns>An enumerator for the structured values.</returns>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Renders the original message followed by the suffix with the suppressed count filled in.
    /// </summary>
    /// <returns>The rendered message.</returns>
    public override string ToString() => _cachedToString ??= string.Concat(_renderOriginal(), _renderSuffix());

    /// <summary>
    /// Gets a name for the suppressed count value that no value of the original message uses, so that sinks binding
    /// values to <c>{OriginalFormat}</c> placeholders by name do not confuse the two.
    /// </summary>
    /// <param name="originalState">The structured state of the original message.</param>
    /// <param name="readableCount">
    /// The number of leading values of <paramref name="originalState"/> that are backed by an argument and can be read
    /// safely; the others fail on access anyway, and so do any sink that enumerates them.
    /// </param>
    /// <returns><c>SuppressedCount</c>, or <c>SuppressedCount_1</c>, <c>SuppressedCount_2</c>, ... if it is taken.</returns>
    private static string GetSuppressedCountName(IReadOnlyList<KeyValuePair<string, object?>> originalState, int readableCount)
    {
        var name = SuppressedCountName;

        for (var number = 1; IsNameTaken(name); number++)
        {
            name = $"{SuppressedCountName}_{number}";
        }

        return name;

        bool IsNameTaken(string candidate)
        {
            for (var i = 0; i < readableCount; i++)
            {
                if (originalState[i].Key == candidate)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// An <see cref="ILogger"/> that records the state and formatter it is given instead of writing them anywhere.
    /// </summary>
    private sealed class StateCapture : ILogger
    {
        /// <summary>
        /// The structured state of the most recently captured message, if it has one.
        /// </summary>
        private IReadOnlyList<KeyValuePair<string, object?>>? _state;

        /// <summary>
        /// A function that renders the most recently captured message with its formatter.
        /// </summary>
        private Func<string>? _render;

        /// <summary>
        /// Captures the state and renderer that the standard <see cref="LoggerExtensions"/> produce for a message.
        /// </summary>
        /// <param name="messageTemplate">The message template.</param>
        /// <param name="args">The message template arguments.</param>
        /// <returns>The structured state of the message and a function that renders it.</returns>
        /// <exception cref="InvalidOperationException">The logging library did not log a structured state.</exception>
        public (IReadOnlyList<KeyValuePair<string, object?>> State, Func<string> Render) Capture(string? messageTemplate, object?[] args)
        {
            _state = null;
            _render = null;

            // The level is irrelevant, since this logger writes nothing.
            this.Log(LogLevel.None, messageTemplate, args);

            return _state is not null && _render is not null
                ? (_state, _render)
                : throw new InvalidOperationException("The logging library did not log a structured message state.");
        }

        /// <summary>
        /// Does not begin a scope.
        /// </summary>
        /// <typeparam name="TState">The type of the scope state.</typeparam>
        /// <param name="state">The scope state.</param>
        /// <returns>Always <see langword="null"/>.</returns>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        /// <summary>
        /// Reports every log level as enabled.
        /// </summary>
        /// <param name="logLevel">The log level.</param>
        /// <returns>Always <see langword="true"/>.</returns>
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <summary>
        /// Records the state and formatter of the message.
        /// </summary>
        /// <typeparam name="TState">The type of the state.</typeparam>
        /// <param name="logLevel">The log level.</param>
        /// <param name="eventId">The event id.</param>
        /// <param name="state">The state of the message.</param>
        /// <param name="exception">The exception, if any.</param>
        /// <param name="formatter">The function that renders the state as a message.</param>
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _state = state as IReadOnlyList<KeyValuePair<string, object?>>;
            _render = () => formatter(state, exception);
        }
    }
}
