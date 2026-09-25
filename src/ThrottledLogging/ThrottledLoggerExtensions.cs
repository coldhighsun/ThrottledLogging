using Microsoft.Extensions.Logging;

namespace ThrottledLogging;

/// <summary>
/// Provides throttled logging extension methods for <see cref="ILogger"/>.
/// </summary>
/// <remarks>
/// Message templates should come from a bounded set of compile-time constants (as with standard
/// <see cref="ILogger"/> usage). The internal suppressed-template cache is bounded, so dynamically built
/// templates do not leak memory, but templates beyond that bound are not cached and cost an extra allocation.
/// </remarks>
public static class ThrottledLoggerExtensions
{
    /// <summary>
    /// Writes a throttled critical log message.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="key">The throttling key used to group repeated log messages.</param>
    /// <param name="interval">The minimum time interval between emitted log messages for the same key.</param>
    /// <param name="messageTemplate">The message template.</param>
    /// <param name="args">The message template arguments.</param>
    public static void LogCriticalThrottled(
        this ILogger logger,
        string key,
        TimeSpan interval,
        string? messageTemplate,
        params ReadOnlySpan<object?> args)
        => LogThrottled(logger, LogLevel.Critical, key, interval, null, messageTemplate, args);

    /// <summary>
    /// Writes a throttled critical log message including exception information.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="key">The throttling key used to group repeated log messages.</param>
    /// <param name="interval">The minimum time interval between emitted log messages for the same key.</param>
    /// <param name="exception">The exception to log.</param>
    /// <param name="messageTemplate">The message template.</param>
    /// <param name="args">The message template arguments.</param>
    public static void LogCriticalThrottled(
        this ILogger logger,
        string key,
        TimeSpan interval,
        Exception? exception,
        string? messageTemplate,
        params ReadOnlySpan<object?> args)
        => LogThrottled(logger, LogLevel.Critical, key, interval, exception, messageTemplate, args);

    /// <summary>
    /// Writes a throttled debug log message.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="key">The throttling key used to group repeated log messages.</param>
    /// <param name="interval">The minimum time interval between emitted log messages for the same key.</param>
    /// <param name="messageTemplate">The message template.</param>
    /// <param name="args">The message template arguments.</param>
    public static void LogDebugThrottled(
        this ILogger logger,
        string key,
        TimeSpan interval,
        string? messageTemplate,
        params ReadOnlySpan<object?> args)
        => LogThrottled(logger, LogLevel.Debug, key, interval, null, messageTemplate, args);

    /// <summary>
    /// Writes a throttled debug log message including exception information.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="key">The throttling key used to group repeated log messages.</param>
    /// <param name="interval">The minimum time interval between emitted log messages for the same key.</param>
    /// <param name="exception">The exception to log.</param>
    /// <param name="messageTemplate">The message template.</param>
    /// <param name="args">The message template arguments.</param>
    public static void LogDebugThrottled(
        this ILogger logger,
        string key,
        TimeSpan interval,
        Exception? exception,
        string? messageTemplate,
        params ReadOnlySpan<object?> args)
        => LogThrottled(logger, LogLevel.Debug, key, interval, exception, messageTemplate, args);

    /// <summary>
    /// Writes a throttled error log message.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="key">The throttling key used to group repeated log messages.</param>
    /// <param name="interval">The minimum time interval between emitted log messages for the same key.</param>
    /// <param name="messageTemplate">The message template.</param>
    /// <param name="args">The message template arguments.</param>
    public static void LogErrorThrottled(
        this ILogger logger,
        string key,
        TimeSpan interval,
        string? messageTemplate,
        params ReadOnlySpan<object?> args)
        => LogThrottled(logger, LogLevel.Error, key, interval, null, messageTemplate, args);

    /// <summary>
    /// Writes a throttled error log message including exception information.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="key">The throttling key used to group repeated log messages.</param>
    /// <param name="interval">The minimum time interval between emitted log messages for the same key.</param>
    /// <param name="exception">The exception to log.</param>
    /// <param name="messageTemplate">The message template.</param>
    /// <param name="args">The message template arguments.</param>
    public static void LogErrorThrottled(
        this ILogger logger,
        string key,
        TimeSpan interval,
        Exception? exception,
        string? messageTemplate,
        params ReadOnlySpan<object?> args)
        => LogThrottled(logger, LogLevel.Error, key, interval, exception, messageTemplate, args);

    /// <summary>
    /// Writes a throttled informational log message.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="key">The throttling key used to group repeated log messages.</param>
    /// <param name="interval">The minimum time interval between emitted log messages for the same key.</param>
    /// <param name="messageTemplate">The message template.</param>
    /// <param name="args">The message template arguments.</param>
    public static void LogInformationThrottled(
        this ILogger logger,
        string key,
        TimeSpan interval,
        string? messageTemplate,
        params ReadOnlySpan<object?> args)
        => LogThrottled(logger, LogLevel.Information, key, interval, null, messageTemplate, args);

    /// <summary>
    /// Writes a throttled informational log message including exception information.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="key">The throttling key used to group repeated log messages.</param>
    /// <param name="interval">The minimum time interval between emitted log messages for the same key.</param>
    /// <param name="exception">The exception to log.</param>
    /// <param name="messageTemplate">The message template.</param>
    /// <param name="args">The message template arguments.</param>
    public static void LogInformationThrottled(
        this ILogger logger,
        string key,
        TimeSpan interval,
        Exception? exception,
        string? messageTemplate,
        params ReadOnlySpan<object?> args)
        => LogThrottled(logger, LogLevel.Information, key, interval, exception, messageTemplate, args);

    /// <summary>
    /// Writes a throttled trace log message.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="key">The throttling key used to group repeated log messages.</param>
    /// <param name="interval">The minimum time interval between emitted log messages for the same key.</param>
    /// <param name="messageTemplate">The message template.</param>
    /// <param name="args">The message template arguments.</param>
    public static void LogTraceThrottled(
        this ILogger logger,
        string key,
        TimeSpan interval,
        string? messageTemplate,
        params ReadOnlySpan<object?> args)
        => LogThrottled(logger, LogLevel.Trace, key, interval, null, messageTemplate, args);

    /// <summary>
    /// Writes a throttled trace log message including exception information.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="key">The throttling key used to group repeated log messages.</param>
    /// <param name="interval">The minimum time interval between emitted log messages for the same key.</param>
    /// <param name="exception">The exception to log.</param>
    /// <param name="messageTemplate">The message template.</param>
    /// <param name="args">The message template arguments.</param>
    public static void LogTraceThrottled(
        this ILogger logger,
        string key,
        TimeSpan interval,
        Exception? exception,
        string? messageTemplate,
        params ReadOnlySpan<object?> args)
        => LogThrottled(logger, LogLevel.Trace, key, interval, exception, messageTemplate, args);

    /// <summary>
    /// Writes a throttled warning log message.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="key">The throttling key used to group repeated log messages.</param>
    /// <param name="interval">The minimum time interval between emitted log messages for the same key.</param>
    /// <param name="messageTemplate">The message template.</param>
    /// <param name="args">The message template arguments.</param>
    public static void LogWarningThrottled(
        this ILogger logger,
        string key,
        TimeSpan interval,
        string? messageTemplate,
        params ReadOnlySpan<object?> args)
        => LogThrottled(logger, LogLevel.Warning, key, interval, null, messageTemplate, args);

    /// <summary>
    /// Writes a throttled warning log message including exception information.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="key">The throttling key used to group repeated log messages.</param>
    /// <param name="interval">The minimum time interval between emitted log messages for the same key.</param>
    /// <param name="exception">The exception to log.</param>
    /// <param name="messageTemplate">The message template.</param>
    /// <param name="args">The message template arguments.</param>
    public static void LogWarningThrottled(
        this ILogger logger,
        string key,
        TimeSpan interval,
        Exception? exception,
        string? messageTemplate,
        params ReadOnlySpan<object?> args)
        => LogThrottled(logger, LogLevel.Warning, key, interval, exception, messageTemplate, args);

    /// <summary>
    /// Gets the throttling manager associated with the specified logger.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <returns>The <see cref="ThrottledLogger"/> associated with the logger.</returns>
    private static ThrottledLogger GetManager(ILogger logger)
        => ThrottledLogger.GetOrCreate(logger);

    /// <summary>
    /// Resets any tracked throttle state for <paramref name="key"/> on the given logger,
    /// so the next throttled call for that key is treated as the first.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="key">The throttling key to reset.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <see langword="null"/>.</exception>
    public static void ResetThrottle(this ILogger logger, string key)
    {
        ArgumentNullException.ThrowIfNull(logger);

        GetManager(logger).Reset(key);
    }

    /// <summary>
    /// Attempts to get the number of log calls currently suppressed for <paramref name="key"/> on the given logger.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="key">The throttling key to query.</param>
    /// <param name="suppressedCount">The number of suppressed calls recorded for the key, if tracked.</param>
    /// <returns><see langword="true"/> if the key is currently tracked; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <see langword="null"/>.</exception>
    public static bool TryGetThrottledSuppressedCount(this ILogger logger, string key, out int suppressedCount)
    {
        ArgumentNullException.ThrowIfNull(logger);

        return GetManager(logger).TryGetSuppressedCount(key, out suppressedCount);
    }

    /// <summary>
    /// Writes a log entry only when the throttling policy allows it.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="level">The log level.</param>
    /// <param name="key">The throttling key used to group repeated log messages.</param>
    /// <param name="interval">The minimum time interval between emitted log messages for the same key.</param>
    /// <param name="exception">The exception to log, if any.</param>
    /// <param name="messageTemplate">The message template.</param>
    /// <param name="args">The message template arguments.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <see langword="null"/>.</exception>
    private static void LogThrottled(
        ILogger logger,
        LogLevel level,
        string key,
        TimeSpan interval,
        Exception? exception,
        string? messageTemplate,
        ReadOnlySpan<object?> args)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (!logger.IsEnabled(level))
        {
            return;
        }

        if (!GetManager(logger).ShouldLog(key, interval, out var suppressed))
        {
            return;
        }

        if (suppressed <= 0)
        {
            logger.Log(level, exception, messageTemplate, args.ToArray());
            return;
        }

        // Wraps the original message's state rather than rewriting its template, so the message renders as it would unsuppressed.
        var state = SuppressedLogValues.Create(messageTemplate, args.ToArray(), suppressed);
        logger.Log(level, default, state, exception, SuppressedLogValues.Formatter);
    }
}
