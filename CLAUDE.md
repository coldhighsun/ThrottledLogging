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

Solution layout: `src/ThrottledLogging/` (library), `tests/ThrottledLogging.Tests/` (xUnit), `benchmarks/ThrottledLogging.Benchmarks/` (BenchmarkDotNet), `examples/GettingStarted/` (console sample).

The library has three source files plus a localized resources directory:

- **`ThrottledLogger.cs`** — Core throttling engine (`public class ThrottledLogger` in namespace `ThrottledLogging`). Uses a `ConditionalWeakTable<ILogger, ThrottledLogger>` to associate one throttler instance per `ILogger` (instances are GC-friendly, tied to the logger's lifetime). Each instance holds a `ConcurrentDictionary<string, Entry>` mapping throttle keys to mutable `Entry` objects holding `(LastLogTick, SuppressedCount, LastSeenTick, Interval)`. A single static `Timer` periodically removes entries that have been idle (measured from `LastSeenTick`, i.e. the last logged *or* suppressed call) longer than the configured expiry (default: 1 hour) *and* whose throttle window (`Interval` since `LastLogTick`) has closed, so an interval longer than the expiry never breaks throttling. Cleanup first *seals* an expired entry (compare-exchanging the suppressed count it read to a sentinel, which fails if a suppressed call counted since) and only then calls the `TryRemove(KeyValuePair)` overload, so an entry refreshed concurrently by `ShouldLog` is not discarded. `SuppressedCount` saturates at `int.MaxValue`. Timestamps use `Stopwatch.GetTimestamp()` / `Stopwatch.GetElapsedTime()` for high-resolution, allocation-free timing. `Entry` is a `private sealed class` so a suppressed call (the hot path in a log storm) updates it in place with `Interlocked` operations and allocates nothing; a struct value would force `ConcurrentDictionary.TryUpdate` to allocate a new node (72 B) per call. The suppressed count is the linearization point: a call that starts a new window, and cleanup, *seal* the entry first (count set to -1), handing the final count to exactly one caller; suppressed calls that find the entry sealed retry the dictionary lookup. `ShouldLog` uses a manual `TryGetValue`/`TryAdd`/`TryUpdate` retry loop rather than `AddOrUpdate`, to avoid delegate/closure allocation. Benchmarks live in `benchmarks/ThrottledLogging.Benchmarks` (`dotnet run -c Release --project benchmarks/ThrottledLogging.Benchmarks`).

- **`ThrottledLoggerExtensions.cs`** — Extension methods on `ILogger` (`LogTraceThrottled`, `LogDebugThrottled`, `LogInformationThrottled`, `LogWarningThrottled`, `LogErrorThrottled`, `LogCriticalThrottled`). Each takes `(string key, TimeSpan interval, ...)` before the standard message/args parameters. When a log is suppressed, the count is incremented. When logging resumes, the message is logged with a `SuppressedLogValues` state instead of the plain template. All extension methods use `params ReadOnlySpan<object?>`.

- **`SuppressedLogValues.cs`** — `internal sealed` `IReadOnlyList<KeyValuePair<string, object?>>` state for a resumed message. `Create` passes the original template/args through MEL's own `LoggerExtensions.Log` into a private capturing `ILogger` (`StateCapture`) to obtain MEL's state and formatter, so the original part renders exactly as it would unsuppressed (no template parsing or brace escaping, independent of MEL version quirks). The rendered message is the original rendering followed by the localized `" ({SuppressedCount} messages suppressed)"` suffix, which is itself rendered through the same capture so its template syntax (format specifiers, escaped braces) matches `{OriginalFormat}`; the structured values are the original ones, then `SuppressedCount` (renamed to `SuppressedCount_1`, `_2`, ... in both the values and the suffix if the original message already has a value by that name), then `{OriginalFormat}` set to the original template + suffix. It is a lazy index-mapped view over MEL's state (only the trailing `{OriginalFormat}` is read eagerly), so a malformed call, e.g. fewer args than placeholders, fails only when rendered or enumerated, exactly as MEL's own state does. A `ConcurrentDictionary` keyed by `(template, suffix)` caches that concatenated `{OriginalFormat}`, bounded at 1024 entries (like MEL's own formatter cache) via a separate `Interlocked` counter, since `ConcurrentDictionary.Count` takes every lock; templates beyond the bound are concatenated on each use.

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
