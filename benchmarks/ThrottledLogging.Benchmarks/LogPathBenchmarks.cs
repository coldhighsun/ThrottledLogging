using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging;

namespace ThrottledLogging.Benchmarks;

/// <summary>
/// Measures the allocations and time of each throttled logging path against a plain <see cref="ILogger"/> call,
/// using a logger that discards its output so only the library's own cost is measured.
/// </summary>
[MemoryDiagnoser]
public class LogPathBenchmarks
{
    /// <summary>
    /// The message template used by every benchmark.
    /// </summary>
    private const string Template = "Disk usage is above {Percent}% on {Volume}";

    /// <summary>
    /// The throttling key used by every benchmark.
    /// </summary>
    private const string Key = "disk-usage";

    /// <summary>
    /// The logger that discards everything, so formatting and output are not part of the measurement.
    /// </summary>
    private readonly ILogger _logger = new NullSinkLogger();

    /// <summary>
    /// The throttler used to measure <see cref="ThrottledLogger.ShouldLog"/> in isolation.
    /// </summary>
    private readonly ThrottledLogger _throttler = new();

    /// <summary>
    /// Establishes a baseline: a plain, unthrottled call with two arguments.
    /// </summary>
    [Benchmark(Baseline = true)]
    public void Plain_LogInformation()
        => _logger.LogInformation(Template, 91, "C:");

    /// <summary>
    /// A throttled call that is always allowed (a zero interval) and has nothing suppressed, i.e. the pass-through path.
    /// </summary>
    [Benchmark]
    public void Throttled_Allowed()
        => _logger.LogInformationThrottled(Key + "-allowed", TimeSpan.Zero, Template, 91, "C:");

    /// <summary>
    /// A throttled call that is suppressed, i.e. the path taken by almost every call in a log storm.
    /// </summary>
    [Benchmark]
    public void Throttled_Suppressed()
        => _logger.LogInformationThrottled(Key + "-suppressed", TimeSpan.FromDays(1), Template, 91, "C:");

    /// <summary>
    /// <see cref="ThrottledLogger.ShouldLog"/> alone on the suppressed path, without any logger call or argument handling.
    /// </summary>
    /// <returns>Whether the call should be logged, so the call is not optimized away.</returns>
    [Benchmark]
    public bool ShouldLog_SuppressedOnly()
        => _throttler.ShouldLog(Key, TimeSpan.FromDays(1), out _);

    /// <summary>
    /// A throttled call that is allowed after suppression, so it carries a suppressed count. The suppressed count is
    /// produced by a second key whose first call is already counted, via a zero interval flip on every invocation.
    /// </summary>
    [Benchmark]
    public void Throttled_ResumedAfterSuppression()
    {
        // Two calls with a long interval produce one suppressed count; the zero-interval call then resumes with it.
        _logger.LogInformationThrottled(Key + "-resumed", TimeSpan.FromDays(1), Template, 91, "C:");
        _logger.LogInformationThrottled(Key + "-resumed", TimeSpan.FromDays(1), Template, 91, "C:");
        _logger.LogInformationThrottled(Key + "-resumed", TimeSpan.Zero, Template, 91, "C:");
    }

    /// <summary>
    /// An <see cref="ILogger"/> that accepts every message and does nothing with it.
    /// </summary>
    private sealed class NullSinkLogger : ILogger
    {
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
        /// Discards the message.
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
        }
    }
}
