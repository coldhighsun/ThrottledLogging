# ThrottledLogging

**English** | [简体中文](https://github.com/coldhighsun/ThrottledLogging/blob/main/README.zh-CN.md)

[![CI](https://github.com/coldhighsun/ThrottledLogging/actions/workflows/ci.yml/badge.svg)](https://github.com/coldhighsun/ThrottledLogging/actions/workflows/ci.yml)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![NuGet Stable](https://img.shields.io/nuget/v/ThrottledLogging?label=NuGet%20Stable)](https://www.nuget.org/packages/ThrottledLogging)
[![NuGet Preview](https://img.shields.io/nuget/vpre/ThrottledLogging?label=NuGet%20Preview)](https://www.nuget.org/packages/ThrottledLogging/absoluteLatest)
[![NuGet Downloads](https://img.shields.io/nuget/dt/ThrottledLogging)](https://www.nuget.org/packages/ThrottledLogging)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![GitHub Stars](https://img.shields.io/github/stars/coldhighsun/ThrottledLogging?style=flat)](https://github.com/coldhighsun/ThrottledLogging/stargazers)
[![GitHub Issues](https://img.shields.io/github/issues/coldhighsun/ThrottledLogging)](https://github.com/coldhighsun/ThrottledLogging/issues)
[![GitHub Open PRs](https://img.shields.io/github/issues-pr/coldhighsun/ThrottledLogging)](https://github.com/coldhighsun/ThrottledLogging/pulls)
[![GitHub last commit](https://img.shields.io/github/last-commit/coldhighsun/ThrottledLogging)](https://github.com/coldhighsun/ThrottledLogging/commits/main)

A time-interval-based log throttler for `Microsoft.Extensions.Logging` that suppresses repeated log entries per key and reports the suppressed count when logging resumes.

## Projects

| Project | Description |
|---|---|
| `src/ThrottledLogging` | NuGet library |
| `examples/GettingStarted` | Console demo covering all features |

## Installation

```
dotnet add package ThrottledLogging
```

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

Each method signature is `(string key, TimeSpan interval, string? messageTemplate, params ReadOnlySpan<object?> args)`. Every method also has an overload that accepts an `Exception?` before the message template, matching the standard `ILogger` pattern:

```csharp
logger.LogErrorThrottled("payment-failed", TimeSpan.FromMinutes(1), exception, "Payment failed for order {OrderId}", orderId);
```

### Suppressed count

When a throttled message is allowed through after one or more suppressions, the suppressed count is automatically appended to the message:

```
Disk usage is above 95% (3 messages suppressed)
```

The suffix is localized. On a `zh-CN` system the output is:

```
Disk usage is above 95% (3个消息被隐藏)
```

To pin the language instead of following the current culture automatically, set `ThrottledLogger.Language`:

```csharp
ThrottledLogger.Language = CultureInfo.GetCultureInfo("zh-Hans");
```

Set it back to `null` (the default) to resume following `CultureInfo.CurrentUICulture` automatically.

### Multiple independent keys

Each key has its own throttle budget — different keys do not share a counter:

```csharp
logger.LogErrorThrottled("service-a-down", TimeSpan.FromSeconds(5), "Service A is unreachable");
logger.LogErrorThrottled("service-b-down", TimeSpan.FromSeconds(5), "Service B is unreachable");
```

### Resetting and inspecting throttle state

Use `ResetThrottle` to manually clear a key's throttle state (the next call is treated as the first), and `TryGetThrottledSuppressedCount` to check how many calls are currently suppressed for a key without logging:

```csharp
logger.ResetThrottle("disk-full");

if (logger.TryGetThrottledSuppressedCount("disk-full", out var suppressed))
{
    Console.WriteLine($"{suppressed} messages currently suppressed");
}
```

### Configuration

Global expiry and cleanup settings can be adjusted at startup:

```csharp
// Expire idle entries after 30 minutes; run cleanup every 15 minutes.
ThrottledLogger.Configure(expiry: TimeSpan.FromMinutes(30), cleanupPeriod: TimeSpan.FromMinutes(15));
```

| Parameter | Default | Description |
|---|---|---|
| `expiry` | 1 hour | How long an idle entry (no logged or suppressed calls) is kept before being removed. Entries still inside their throttle interval are never removed, so avoid very long intervals with an unbounded set of keys. |
| `cleanupPeriod` | 1 hour | How often the background cleanup timer runs. Must be at least 1 ms and at most about 49.7 days (4294967294 ms); pass `Timeout.InfiniteTimeSpan` to disable cleanup. |

## Requirements

.NET 10 or later.

## Build

```bash
dotnet build
dotnet test
dotnet pack
```

`dotnet test` runs via [Microsoft.Testing.Platform](https://aka.ms/testingplatform) (MTP), configured in `global.json`.
