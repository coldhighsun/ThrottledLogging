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

The library, tests, and examples all target `net10.0`. Build artifacts go to `artifacts/` (configured via `Directory.Build.props`).

## Architecture

Solution layout: `src/ThrottledLogging/` (library), `tests/ThrottledLogging.Tests/` (xUnit), `examples/GettingStarted/` (console sample).

The library has two source files plus a localized resources directory:

- **`ThrottledLogger.cs`** — Core throttling engine (`public class ThrottledLogger` in namespace `ThrottledLogging`). Uses a `ConditionalWeakTable<ILogger, ThrottledLogger>` to associate one throttler instance per `ILogger` (instances are GC-friendly, tied to the logger's lifetime). Each instance holds a `ConcurrentDictionary<string, Entry>` mapping throttle keys to `(LastLogTick, SuppressedCount)` records. A single static `Timer` periodically cleans up entries idle longer than the configured expiry (default: 1 hour). Timestamps use `Stopwatch.GetTimestamp()` / `Stopwatch.GetElapsedTime()` for high-resolution, allocation-free timing. `ShouldLog` updates `_tracker` via a manual `TryGetValue`/`TryUpdate`/`TryAdd` CAS retry loop rather than `AddOrUpdate`, to avoid the delegate/closure allocation `AddOrUpdate`'s factory-based API would incur on every call; `Entry` implements `IEquatable<Entry>` so `TryUpdate`'s comparand check doesn't box.

- **`ThrottledLoggerExtensions.cs`** — Extension methods on `ILogger` (`LogTraceThrottled`, `LogDebugThrottled`, `LogInformationThrottled`, `LogWarningThrottled`, `LogErrorThrottled`, `LogCriticalThrottled`). Each takes `(string key, TimeSpan interval, ...)` before the standard message/args parameters. When a log is suppressed, the count is incremented. When logging resumes, the suppressed count is appended to the message template as `" ({SuppressedCount} messages suppressed)"`. A `ConcurrentDictionary` caches the concatenated suppressed templates to avoid repeated string allocations. All extension methods use `params ReadOnlySpan<object?>`.

- **`Resources/`** — `Messages.resx` (default/English) and `Messages.zh-CN.resx` (Simplified Chinese) hold the localizable suppressed-count suffix string. `Messages.Designer.cs` is auto-generated; edit the `.resx` files to change or add translations.

**Key design decisions:**
- `ConditionalWeakTable` means no explicit registration/disposal — throttlers are created on demand and cleaned up when the `ILogger` is GC'd.
- `ThrottledLogger` also has a `public` constructor, used directly in unit tests to exercise `ShouldLog` without involving the static `_instances` table.
- `ThrottledLogger.Configure(expiry, cleanupPeriod)` is a global static setting affecting all instances. Tests that rely on timing should avoid calling it, or restore defaults afterwards.
- Version is managed by [MinVer](https://github.com/adamralph/minver) from git tags (prefix `v`).
- Central package management via `Directory.Packages.props`.
- `WarningsAsErrors` is enabled globally.
- CI runs on GitHub Actions (`.github/workflows/ci.yml`): build + test on every push/PR to `main`. Pushing a `v*` tag triggers pack and publish to NuGet via `secrets.NUGET_API_KEY`.

## Tests

`FakeLogger` (`tests/.../FakeLogger.cs`) is a minimal `ILogger` that records `(LogLevel, Message)` entries and supports a configurable `MinLevel`. Use it in tests that need to assert on emitted messages or log levels.

When asserting on suppressed-count message strings (e.g. `"(3 messages suppressed)"`), pin the culture in a try/finally block:
```csharp
Messages.Culture = CultureInfo.InvariantCulture;
try { /* assert */ }
finally { Messages.Culture = null; }
```
The suffix is localized and will differ on zh-CN systems without this.

To force a throttle to allow without sleeping, pass `TimeSpan.Zero` as the interval — `ShouldLog` always returns `true` for a zero interval and reports the accumulated suppressed count.

Tests that call `ThrottledLogger.Configure()` must be placed in the `[Collection("Sequential")]` xUnit collection (defined in `CollectionDefinitions.cs`) and must restore defaults in a `finally` block — `Configure()` is a global static and would otherwise pollute parallel test runs.
