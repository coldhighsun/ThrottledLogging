using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace ThrottledLogging;

/// <summary>
/// Provides throttled logging extension methods for <see cref="ILogger"/>.
/// </summary>
public static class ThrottledLoggerExtensions
{
    /// <summary>
    /// Represents the suffix appended to a message when suppressed messages are present.
    /// </summary>
    private const string SuppressedSuffix = " ({SuppressedCount} messages suppressed)";

    /// <summary>
    /// Caches message templates with the suppressed count placeholder to avoid repeated string concatenation for the same template.
    /// </summary>
    private static readonly ConcurrentDictionary<string, string> SuppressedTemplateCache = new ConcurrentDictionary<string, string>();

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
#if NET9_0_OR_GREATER
        params ReadOnlySpan<object?> args)
#else
        params object?[] args)
#endif
        => LogThrottled(logger, LogLevel.Critical, key, interval, messageTemplate, args);

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
#if NET9_0_OR_GREATER
        params ReadOnlySpan<object?> args)
#else
        params object?[] args)
#endif
        => LogThrottled(logger, LogLevel.Debug, key, interval, messageTemplate, args);

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
#if NET9_0_OR_GREATER
        params ReadOnlySpan<object?> args)
#else
        params object?[] args)
#endif
        => LogThrottled(logger, LogLevel.Error, key, interval, messageTemplate, args);

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
#if NET9_0_OR_GREATER
        params ReadOnlySpan<object?> args)
#else
        params object?[] args)
#endif
        => LogThrottled(logger, LogLevel.Information, key, interval, messageTemplate, args);

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
#if NET9_0_OR_GREATER
        params ReadOnlySpan<object?> args)
#else
        params object?[] args)
#endif
        => LogThrottled(logger, LogLevel.Trace, key, interval, messageTemplate, args);

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
#if NET9_0_OR_GREATER
        params ReadOnlySpan<object?> args)
#else
        params object?[] args)
#endif
        => LogThrottled(logger, LogLevel.Warning, key, interval, messageTemplate, args);

    /// <summary>
    /// Appends the suppressed message count to the existing logging arguments.
    /// </summary>
    /// <param name="args">The original logging arguments.</param>
    /// <param name="suppressed">The number of suppressed messages.</param>
    /// <returns>A new array containing the original arguments followed by the suppressed count.</returns>
    private static object?[] AppendSuppressed(
#if NET9_0_OR_GREATER
        ReadOnlySpan<object?> args,
#else
        object?[] args,
#endif
        int suppressed)
    {
        var combined = new object?[args.Length + 1];
#if NET9_0_OR_GREATER
        args.CopyTo(combined);
#else
        Array.Copy(args, combined, args.Length);
#endif
        combined[args.Length] = suppressed;
        return combined;
    }

    /// <summary>
    /// Gets the throttling manager associated with the specified logger.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <returns>The <see cref="ThrottledLogger"/> associated with the logger.</returns>
    private static ThrottledLogger GetManager(ILogger logger)
        => ThrottledLogger.GetOrCreate(logger);

    /// <summary>
    /// Gets the message template that includes the suppressed message count placeholder.
    /// </summary>
    /// <param name="messageTemplate">The original message template.</param>
    /// <returns>
    /// The original message template with an appended suppressed count placeholder,
    /// or the suppressed suffix when the original template is <see langword="null"/>.
    /// </returns>
    private static string GetSuppressedTemplate(string? messageTemplate)
        => messageTemplate is null
            ? SuppressedSuffix
            : SuppressedTemplateCache.GetOrAdd(messageTemplate, t => string.Concat(t, SuppressedSuffix));

    /// <summary>
    /// Writes a log entry only when the throttling policy allows it.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="level">The log level.</param>
    /// <param name="key">The throttling key used to group repeated log messages.</param>
    /// <param name="interval">The minimum time interval between emitted log messages for the same key.</param>
    /// <param name="messageTemplate">The message template.</param>
    /// <param name="args">The message template arguments.</param>
    private static void LogThrottled(
        ILogger logger,
        LogLevel level,
        string key,
        TimeSpan interval,
        string? messageTemplate,
#if NET9_0_OR_GREATER
        ReadOnlySpan<object?> args)
#else
        object?[] args)
#endif
    {
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
#if NET9_0_OR_GREATER
            logger.Log(level, messageTemplate, args.ToArray());
#else
            logger.Log(level, messageTemplate, args);
#endif
            return;
        }

        logger.Log(level, GetSuppressedTemplate(messageTemplate), AppendSuppressed(args, suppressed));
    }
}
