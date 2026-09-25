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

The library has three source files plus a localized resources directory:

- **`ThrottledLogger.cs`** — Core throttling engine (`public class ThrottledLogger` in namespace `ThrottledLogging`). Uses a `ConditionalWeakTable<ILogger, ThrottledLogger>` to associate one throttler instance per `ILogger` (instances are GC-friendly, tied to the logger's lifetime). Each instance holds a `ConcurrentDictionary<string, Entry>` mapping throttle keys to `(LastLogTick, SuppressedCount, LastSeenTick, Interval)` records. A single static `Timer` periodically removes entries that have been idle (measured from `LastSeenTick`, i.e. the last logged *or* suppressed call) longer than the configured expiry (default: 1 hour) *and* whose throttle window (`Interval` since `LastLogTick`) has closed, so an interval longer than the expiry never breaks throttling. Cleanup uses the value-comparing `TryRemove(KeyValuePair)` overload so an entry refreshed concurrently by `ShouldLog` is not discarded. `SuppressedCount` saturates at `int.MaxValue`. Timestamps use `Stopwatch.GetTimestamp()` / `Stopwatch.GetElapsedTime()` for high-resolution, allocation-free timing. `ShouldLog` updates `_tracker` via a manual `TryGetValue`/`TryUpdate`/`TryAdd` CAS retry loop rather than `AddOrUpdate`, to avoid the delegate/closure allocation `AddOrUpdate`'s factory-based API would incur on every call; `Entry` implements `IEquatable<Entry>` so `TryUpdate`'s comparand check doesn't box.

- **`ThrottledLoggerExtensions.cs`** — Extension methods on `ILogger` (`LogTraceThrottled`, `LogDebugThrottled`, `LogInformationThrottled`, `LogWarningThrottled`, `LogErrorThrottled`, `LogCriticalThrottled`). Each takes `(string key, TimeSpan interval, ...)` before the standard message/args parameters. When a log is suppressed, the count is incremented. When logging resumes, the message is logged with a `SuppressedLogValues` state instead of the plain template. All extension methods use `params ReadOnlySpan<object?>`.

- **`SuppressedLogValues.cs`** — `internal sealed` `IReadOnlyList<KeyValuePair<string, object?>>` state for a resumed message. `Create` passes the original template/args through MEL's own `LoggerExtensions.Log` into a private capturing `ILogger` (`StateCapture`) to obtain MEL's state and formatter, so the original part renders exactly as it would unsuppressed (no template parsing or brace escaping, independent of MEL version quirks). The rendered message is the original rendering followed by the localized `" ({SuppressedCount} messages suppressed)"` suffix, which is itself rendered through the same capture so its template syntax (format specifiers, escaped braces) matches `{OriginalFormat}`; the structured values are the original ones, then `SuppressedCount` (renamed to `SuppressedCount_1`, `_2`, ... in both the values and the suffix if the original message already has a value by that name), then `{OriginalFormat}` set to the original template + suffix. It is a lazy index-mapped view over MEL's state (only the trailing `{OriginalFormat}` is read eagerly), so a malformed call, e.g. fewer args than placeholders, fails only when rendered or enumerated, exactly as MEL's own state does. A `ConcurrentDictionary` keyed by `(template, suffix)` caches that concatenated `{OriginalFormat}`.

- **`Resources/`** — `Messages.resx` (default/English) and `Messages.zh-Hans.resx` (Simplified Chinese; also used for zh-CN, zh-SG via culture fallback) hold the localizable suppressed-count suffix string. `Messages.Designer.cs` is auto-generated; edit the `.resx` files to change or add translations.

**Key design decisions:**
- `ConditionalWeakTable` means no explicit registration/disposal — throttlers are created on demand and cleaned up when the `ILogger` is GC'd.
- `ThrottledLogger` also has a `public` constructor, used directly in unit tests to exercise `ShouldLog` without involving the static `_instances` table.
- `ThrottledLogger.Configure(expiry, cleanupPeriod)` is a global static setting affecting all instances. It validates arguments before changing anything: `expiry` must be non-negative; `cleanupPeriod` must be at least 1 ms (`Timer` truncates to whole milliseconds) and at most 4294967294 ms (the `Timer` limit), or `Timeout.InfiniteTimeSpan` to disable cleanup. Tests that rely on timing should avoid calling it, or restore defaults afterwards.
- Version is managed by [MinVer](https://github.com/adamralph/minver) from git tags (prefix `v`).
- Central package management via `Directory.Packages.props`.
- `WarningsAsErrors` is enabled globally.
- CI runs on GitHub Actions (`.github/workflows/ci.yml`): build + test on every push/PR to `main`. Pushing a `v*` tag triggers pack and publish to NuGet via `secrets.NUGET_API_KEY`.

## Tests

`FakeLogger` (`tests/.../FakeLogger.cs`) is a minimal `ILogger` that records `(LogLevel, Message, State)` entries (`State` is a snapshot of the structured values, for asserting on `{OriginalFormat}` and `SuppressedCount`) and supports a configurable `MinLevel`. Use it in tests that need to assert on emitted messages or log levels.

When asserting on suppressed-count message strings (e.g. `"(3 messages suppressed)"`), pin the culture in a try/finally block:
```csharp
Messages.Culture = CultureInfo.InvariantCulture;
try { /* assert */ }
finally { Messages.Culture = null; }
```
The suffix is localized and will differ on zh-CN systems without this.

To force a throttle to allow without sleeping, pass `TimeSpan.Zero` as the interval — `ShouldLog` always returns `true` for a zero interval and reports the accumulated suppressed count.

Tests that call `ThrottledLogger.Configure()` must be placed in the `[Collection("Sequential")]` xUnit collection (defined in `CollectionDefinitions.cs`) and must restore defaults in a `finally` block — `Configure()` is a global static and would otherwise pollute parallel test runs.
