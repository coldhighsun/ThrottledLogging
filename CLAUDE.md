# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build
dotnet build

# Run tests
dotnet test

# Run a single test
dotnet test --filter "FullyQualifiedName~TestMethodName"

# Pack NuGet package
dotnet pack
```

Targets both `net9.0` and `net10.0`. Build artifacts go to `artifacts/` (configured via `Directory.Build.props`).

## Architecture

This is a single-project NuGet library (`src/ThrottledLogging/`) with two source files:

- **`ThrottledLogger.cs`** — Core throttling engine (`public class ThrottledLogger` in namespace `ThrottledLogging`). Uses a `ConditionalWeakTable<ILogger, ThrottledLogger>` to associate one throttler instance per `ILogger` (instances are GC-friendly, tied to the logger's lifetime). Each instance holds a `ConcurrentDictionary<string, Entry>` mapping throttle keys to `(LastLogTick, SuppressedCount)` records. A single static `Timer` periodically cleans up entries idle longer than the configured expiry (default: 1 hour). Timestamps use `Stopwatch.GetTimestamp()` / ticks for high-resolution, allocation-free timing.

- **`ThrottledLoggerExtensions.cs`** — Extension methods on `ILogger` (`LogTraceThrottled`, `LogDebugThrottled`, `LogInformationThrottled`, `LogWarningThrottled`, `LogErrorThrottled`, `LogCriticalThrottled`). Each takes `(string key, TimeSpan interval, ...)` before the standard message/args parameters. When a log is suppressed, the count is incremented. When logging resumes, the suppressed count is appended to the message template as `" ({SuppressedCount} messages suppressed)"`. A `ConcurrentDictionary` caches the concatenated suppressed templates to avoid repeated string allocations.

**Key design decisions:**
- `ConditionalWeakTable` means no explicit registration/disposal — throttlers are created on demand and cleaned up when the `ILogger` is GC'd.
- `ThrottledLogger.Configure(expiry, cleanupPeriod)` is a global static setting affecting all instances.
- Version is managed by [MinVer](https://github.com/adamralph/minver) from git tags (prefix `v`).
- Central package management via `Directory.Packages.props`.
- `WarningsAsErrors` is enabled globally.
