using Microsoft.Extensions.Logging;

namespace ThrottledLogging.Tests;

/// <summary>
/// A minimal <see cref="ILogger"/> that records every entry it is given, for asserting on emitted messages.
/// </summary>
internal sealed class FakeLogger : ILogger
{
    /// <summary>
    /// A recorded log entry.
    /// </summary>
    /// <param name="Level">The log level of the entry.</param>
    /// <param name="Message">The rendered message of the entry.</param>
    /// <param name="State">The structured values of the entry, if its state has any.</param>
    public record struct LogEntry(LogLevel Level, string? Message, IReadOnlyList<KeyValuePair<string, object?>>? State);

    /// <summary>
    /// Gets the recorded entries, in the order they were logged.
    /// </summary>
    public List<LogEntry> Entries { get; } = [];

    /// <summary>
    /// Gets or sets the minimum log level reported as enabled.
    /// </summary>
    public LogLevel MinLevel { get; set; } = LogLevel.Trace;

    /// <summary>
    /// Does not begin a scope.
    /// </summary>
    /// <typeparam name="TState">The type of the scope state.</typeparam>
    /// <param name="state">The scope state.</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    /// <summary>
    /// Reports whether <paramref name="logLevel"/> is at or above <see cref="MinLevel"/>.
    /// </summary>
    /// <param name="logLevel">The log level to check.</param>
    /// <returns><see langword="true"/> if the level is enabled; otherwise <see langword="false"/>.</returns>
    public bool IsEnabled(LogLevel logLevel) => logLevel >= MinLevel;

    /// <summary>
    /// Records the entry with its rendered message and structured values.
    /// </summary>
    /// <typeparam name="TState">The type of the state.</typeparam>
    /// <param name="logLevel">The log level.</param>
    /// <param name="eventId">The event id.</param>
    /// <param name="state">The state of the entry.</param>
    /// <param name="exception">The exception, if any.</param>
    /// <param name="formatter">The function that renders the state as a message.</param>
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add(new(logLevel, formatter(state, exception), [.. state as IReadOnlyList<KeyValuePair<string, object?>> ?? []]));
    }
}
