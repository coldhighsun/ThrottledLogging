# ThrottledLogging

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
| `cleanupPeriod` | 1 hour | How often the background cleanup timer runs |

## Requirements

.NET 10 or later.

## Build

```bash
dotnet build
dotnet test
dotnet pack
```

`dotnet test` runs via [Microsoft.Testing.Platform](https://aka.ms/testingplatform) (MTP), configured in `global.json`.

---

# ThrottledLogging（中文）

基于时间间隔的 `Microsoft.Extensions.Logging` 日志限流器，可按 key 抑制重复日志，并在恢复输出时报告被抑制的条数。

## 项目结构

| 项目 | 说明 |
|---|---|
| `src/ThrottledLogging` | NuGet 库 |
| `examples/GettingStarted` | 控制台示例，涵盖所有功能 |

## 安装

```
dotnet add package ThrottledLogging
```

## 用法

在任意 `ILogger` 上调用限流扩展方法。每次调用需传入一个 **key**（用于标识被限流的日志消息）和一个 **interval**（两次输出之间的最短间隔）。

```csharp
// "disk-full" 这个 key 每分钟最多输出一次。
// 抑制后下次恢复输出时，会自动追加"(N messages suppressed)"。
logger.LogWarningThrottled("disk-full", TimeSpan.FromMinutes(1), "Disk usage is above {Percent}%", usage);
```

可用方法与标准 `ILogger` API 一一对应：

- `LogTraceThrottled`
- `LogDebugThrottled`
- `LogInformationThrottled`
- `LogWarningThrottled`
- `LogErrorThrottled`
- `LogCriticalThrottled`

每个方法的签名为 `(string key, TimeSpan interval, string? messageTemplate, params ReadOnlySpan<object?> args)`。每个方法还有一个在 messageTemplate 之前接受 `Exception?` 的重载，与标准 `ILogger` 的用法一致：

```csharp
logger.LogErrorThrottled("payment-failed", TimeSpan.FromMinutes(1), exception, "Payment failed for order {OrderId}", orderId);
```

### 抑制计数

当某条被限流的消息在经历一次或多次抑制后重新允许输出时，抑制次数会自动追加到消息末尾。追加的后缀已本地化，在 `zh-CN` 环境下输出为：

```
Disk usage is above 95% (3个消息被隐藏)
```

### 多个独立 key

每个 key 拥有独立的限流计数器，互不干扰：

```csharp
logger.LogErrorThrottled("service-a-down", TimeSpan.FromSeconds(5), "Service A is unreachable");
logger.LogErrorThrottled("service-b-down", TimeSpan.FromSeconds(5), "Service B is unreachable");
```

### 重置与查询限流状态

使用 `ResetThrottle` 可以手动清除某个 key 的限流状态（下一次调用会被当作首次调用处理）；使用 `TryGetThrottledSuppressedCount` 可以在不写日志的情况下查询某个 key 当前被抑制的次数：

```csharp
logger.ResetThrottle("disk-full");

if (logger.TryGetThrottledSuppressedCount("disk-full", out var suppressed))
{
    Console.WriteLine($"当前有 {suppressed} 条消息被抑制");
}
```

### 全局配置

可在应用启动时调整全局的过期时间和清理周期：

```csharp
// 空闲条目 30 分钟后过期；每 15 分钟执行一次清理。
ThrottledLogger.Configure(expiry: TimeSpan.FromMinutes(30), cleanupPeriod: TimeSpan.FromMinutes(15));
```

| 参数 | 默认值 | 说明 |
|---|---|---|
| `expiry` | 1 小时 | 空闲条目（无输出也无被抑制的调用）在被移除前的保留时长。仍处于节流间隔内的条目不会被移除，因此应避免将超长间隔与数量无上限的 key 组合使用。 |
| `cleanupPeriod` | 1 小时 | 后台清理定时器的运行间隔 |

## 环境要求

.NET 10 或更高版本。

## 构建

```bash
dotnet build
dotnet test
dotnet pack
```

`dotnet test` 通过 [Microsoft.Testing.Platform](https://aka.ms/testingplatform)（MTP）运行，相关配置见 `global.json`。
