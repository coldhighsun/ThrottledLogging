using Microsoft.Extensions.Logging;
using ThrottledLogging;

// Set up a console logger (synchronous write so output stays in order)
using var loggerFactory = LoggerFactory.Create(builder =>
    builder.AddSimpleConsole(options =>
    {
        options.TimestampFormat = "HH:mm:ss.fff ";
        options.SingleLine = true;
    }));

var logger = loggerFactory.CreateLogger("Demo");

// Give the async console queue a moment to drain between sections.
static void Drain() => Thread.Sleep(50);

// -----------------------------------------------------------------------
// Example 1: Basic throttling
// Only the first call in each 1-second window is emitted.
// The rest are suppressed and their count is reported on the next emission.
// -----------------------------------------------------------------------
Console.WriteLine("=== Example 1: Basic throttling (interval = 1 s) ===");
Console.WriteLine("Sending 5 rapid calls, then waiting 1.1 s for the window to expire...");
Console.WriteLine();

for (var i = 1; i <= 5; i++)
{
    logger.LogWarningThrottled("disk-usage", TimeSpan.FromSeconds(1),
        "Disk usage is above {Percent}%", 90 + i);
}

Thread.Sleep(TimeSpan.FromSeconds(1.1));

// This call resumes logging and reports the 4 suppressed messages.
logger.LogWarningThrottled("disk-usage", TimeSpan.FromSeconds(1),
    "Disk usage is above {Percent}%", 96);

Drain();
Console.WriteLine();

// -----------------------------------------------------------------------
// Example 2: Multiple independent keys
// Each key has its own throttle budget — they don't share a counter.
// -----------------------------------------------------------------------
Console.WriteLine("=== Example 2: Multiple keys throttled independently ===");
Console.WriteLine();

for (var i = 0; i < 3; i++)
{
    logger.LogErrorThrottled("service-a-down", TimeSpan.FromSeconds(5),
        "Service A is unreachable (attempt {Attempt})", i + 1);

    logger.LogErrorThrottled("service-b-down", TimeSpan.FromSeconds(5),
        "Service B is unreachable (attempt {Attempt})", i + 1);
}

Drain();
Console.WriteLine();

// -----------------------------------------------------------------------
// Example 3: All log levels
// -----------------------------------------------------------------------
Console.WriteLine("=== Example 3: All log levels ===");
Console.WriteLine();

logger.LogTraceThrottled("trace-key",       TimeSpan.FromSeconds(5), "Trace message");
logger.LogDebugThrottled("debug-key",       TimeSpan.FromSeconds(5), "Debug message");
logger.LogInformationThrottled("info-key",  TimeSpan.FromSeconds(5), "Information message");
logger.LogWarningThrottled("warn-key",      TimeSpan.FromSeconds(5), "Warning message");
logger.LogErrorThrottled("error-key",       TimeSpan.FromSeconds(5), "Error message");
logger.LogCriticalThrottled("critical-key", TimeSpan.FromSeconds(5), "Critical message");

Drain();
Console.WriteLine();

// -----------------------------------------------------------------------
// Example 4: Adjusting global configuration
// Shorten expiry and cleanup period (useful for high-throughput services).
// -----------------------------------------------------------------------
Console.WriteLine("=== Example 4: Custom global configuration ===");
Console.WriteLine();

ThrottledLogger.Configure(
    expiry: TimeSpan.FromMinutes(30),
    cleanupPeriod: TimeSpan.FromMinutes(15));

logger.LogInformationThrottled("config-demo", TimeSpan.FromMilliseconds(200),
    "Configured with 30-minute expiry");

Drain();
Console.WriteLine();

// -----------------------------------------------------------------------
// Example 5: Logging an exception
// Every level has an overload that accepts an Exception before the
// message template, matching the standard ILogger pattern.
// -----------------------------------------------------------------------
Console.WriteLine("=== Example 5: Logging an exception ===");
Console.WriteLine();

try
{
    throw new InvalidOperationException("Simulated payment gateway timeout");
}
catch (Exception ex)
{
    logger.LogErrorThrottled("payment-failed", TimeSpan.FromSeconds(5), ex,
        "Payment failed for order {OrderId}", 1234);
}

Drain();
Console.WriteLine();

// -----------------------------------------------------------------------
// Example 6: Resetting and inspecting throttle state
// ResetThrottle clears a key's state early; TryGetThrottledSuppressedCount
// inspects the current suppressed count without emitting a log entry.
// -----------------------------------------------------------------------
Console.WriteLine("=== Example 6: Resetting and inspecting throttle state ===");
Console.WriteLine();

logger.LogWarningThrottled("reset-demo", TimeSpan.FromMinutes(1), "First call");
logger.LogWarningThrottled("reset-demo", TimeSpan.FromMinutes(1), "Suppressed call");

if (logger.TryGetThrottledSuppressedCount("reset-demo", out var suppressedCount))
{
    Console.WriteLine($"Currently suppressed: {suppressedCount}");
}

logger.ResetThrottle("reset-demo");
logger.LogWarningThrottled("reset-demo", TimeSpan.FromMinutes(1), "Treated as first call again after reset");

Drain();
Console.WriteLine("Done.");
