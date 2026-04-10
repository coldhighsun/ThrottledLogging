# ThrottledLogger

A time-interval-based log throttler for `Microsoft.Extensions.Logging` that suppresses repeated log entries per key and reports the suppressed count when logging resumes.

## Projects

| Project | Description |
|---|---|
| `src/ThrottledLogging` | NuGet library |
| `examples/GettingStarted` | Console demo covering all features |

## Usage

Call the throttled extension methods on any `ILogger`. Each call requires a **key** (identifies the log message for throttling purposes) and an **interval** (minimum time between emissions).

```csharp
// Only logs once per minute for the key "disk-full".
// On the next emission after suppression, appends "(N messages suppressed)".
logger.LogWarningThrottled("disk-full", TimeSpan.FromMinutes(1), "Disk usage is above {Percent}%", usage);
```

Available methods mirror the standard `ILogger` API:

- `LogTraceThrottled`
- `LogDebugThrottled`
- `LogInformationThrottled`
- `LogWarningThrottled`
- `LogErrorThrottled`
- `LogCriticalThrottled`

Each method signature is `(string key, TimeSpan interval, string? messageTemplate, params ReadOnlySpan<object?> args)`.

### Suppressed count

When a throttled message is allowed through after one or more suppressions, the suppressed count is automatically appended to the message:

```
Disk usage is above 95% (3 messages suppressed)
```

### Multiple independent keys

Each key has its own throttle budget — different keys do not share a counter:

```csharp
logger.LogErrorThrottled("service-a-down", TimeSpan.FromSeconds(5), "Service A is unreachable");
logger.LogErrorThrottled("service-b-down", TimeSpan.FromSeconds(5), "Service B is unreachable");
```

### Configuration

Global expiry and cleanup settings can be adjusted at startup:

```csharp
// Expire idle entries after 30 minutes; run cleanup every 15 minutes.
ThrottledLogger.Configure(expiry: TimeSpan.FromMinutes(30), cleanupPeriod: TimeSpan.FromMinutes(15));
```

| Parameter | Default | Description |
|---|---|---|
| `expiry` | 1 hour | How long an idle entry is kept before being removed |
| `cleanupPeriod` | 1 hour | How often the background cleanup timer runs |

## Requirements

- .NET 9 or .NET 10

## Build

```bash
dotnet build
dotnet test
dotnet pack
```
