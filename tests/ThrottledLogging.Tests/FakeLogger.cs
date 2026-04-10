using Microsoft.Extensions.Logging;

namespace ThrottledLogging.Tests;

internal sealed class FakeLogger : ILogger
{
    public record struct LogEntry(LogLevel Level, string? Message);

    public List<LogEntry> Entries { get; } = [];
    public LogLevel MinLevel { get; set; } = LogLevel.Trace;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= MinLevel;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Entries.Add(new(logLevel, formatter(state, exception)));
    }
}