# ThrottledLogger

A time-interval-based log throttler for `Microsoft.Extensions.Logging` that suppresses repeated log entries per key and reports the suppressed count when logging resumes.

## Installation

```
dotnet add package ThrottledLogger
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

## Configuration

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

---

# ThrottledLogger（中文）

基于时间间隔的 `Microsoft.Extensions.Logging` 日志限流器，可按 key 抑制重复日志，并在恢复输出时报告被抑制的条数。

## 安装

```
dotnet add package ThrottledLogger
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

每个方法的签名为 `(string key, TimeSpan interval, string? messageTemplate, params ReadOnlySpan<object?> args)`。

### 抑制计数

当某条被限流的消息在经历一次或多次抑制后重新允许输出时，抑制次数会自动追加到消息末尾：

```
Disk usage is above 95% (3 messages suppressed)
```

### 多个独立 key

每个 key 拥有独立的限流计数器，互不干扰：

```csharp
logger.LogErrorThrottled("service-a-down", TimeSpan.FromSeconds(5), "Service A is unreachable");
logger.LogErrorThrottled("service-b-down", TimeSpan.FromSeconds(5), "Service B is unreachable");
```

## 全局配置

可在应用启动时调整全局的过期时间和清理周期：

```csharp
// 空闲条目 30 分钟后过期；每 15 分钟执行一次清理。
ThrottledLogger.Configure(expiry: TimeSpan.FromMinutes(30), cleanupPeriod: TimeSpan.FromMinutes(15));
```

| 参数 | 默认值 | 说明 |
|---|---|---|
| `expiry` | 1 小时 | 空闲条目在被移除前的保留时长 |
| `cleanupPeriod` | 1 小时 | 后台清理定时器的运行间隔 |

## 环境要求

- .NET 9 或 .NET 10
