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

The library targets `netstandard2.0;net8.0;net9.0;net10.0`; tests and examples target `net8.0;net9.0;net10.0`. Build artifacts go to `artifacts/` (configured via `Directory.Build.props`).

## Architecture

Solution layout: `src/ThrottledLogging/` (library), `tests/ThrottledLogging.Tests/` (xUnit), `examples/GettingStarted/` (console sample).

The library has two source files:

- **`ThrottledLogger.cs`** — Core throttling engine (`public class ThrottledLogger` in namespace `ThrottledLogging`). Uses a `ConditionalWeakTable<ILogger, ThrottledLogger>` to associate one throttler instance per `ILogger` (instances are GC-friendly, tied to the logger's lifetime). Each instance holds a `ConcurrentDictionary<string, Entry>` mapping throttle keys to `(LastLogTick, SuppressedCount)` records. A single static `Timer` periodically cleans up entries idle longer than the configured expiry (default: 1 hour). Timestamps use `Stopwatch.GetTimestamp()` / ticks for high-resolution, allocation-free timing.

- **`ThrottledLoggerExtensions.cs`** — Extension methods on `ILogger` (`LogTraceThrottled`, `LogDebugThrottled`, `LogInformationThrottled`, `LogWarningThrottled`, `LogErrorThrottled`, `LogCriticalThrottled`). Each takes `(string key, TimeSpan interval, ...)` before the standard message/args parameters. When a log is suppressed, the count is incremented. When logging resumes, the suppressed count is appended to the message template as `" ({SuppressedCount} messages suppressed)"`. A `ConcurrentDictionary` caches the concatenated suppressed templates to avoid repeated string allocations.

**Key design decisions:**
- `ConditionalWeakTable` means no explicit registration/disposal — throttlers are created on demand and cleaned up when the `ILogger` is GC'd.
- `ThrottledLogger` also has a `public` constructor, used directly in unit tests to exercise `ShouldLog` without involving the static `_instances` table.
- `ThrottledLogger.Configure(expiry, cleanupPeriod)` is a global static setting affecting all instances. Tests that rely on timing should avoid calling it, or restore defaults afterwards.
- `interval.Ticks` is compared directly against `Stopwatch` ticks — this works because `TimeSpan.Ticks` and `Stopwatch` ticks are both 100 ns on .NET (they share the same tick frequency).
- Version is managed by [MinVer](https://github.com/adamralph/minver) from git tags (prefix `v`).
- Central package management via `Directory.Packages.props`.
- `WarningsAsErrors` is enabled globally.

## Tests

`FakeLogger` (`tests/.../FakeLogger.cs`) is a minimal `ILogger` that records `(LogLevel, Message)` entries and supports a configurable `MinLevel`. Use it in tests that need to assert on emitted messages or log levels.
