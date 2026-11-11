# ThrottledLogging

[English](README.md) | [简体中文](README.zh-CN.md) | **繁體中文**

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

基於時間間隔的 `Microsoft.Extensions.Logging` 日誌限流器，可依 key 抑制重複日誌，並在恢復輸出時回報被抑制的筆數。

## 專案結構

| 專案 | 說明 |
|---|---|
| `src/ThrottledLogging` | NuGet 程式庫 |
| `examples/GettingStarted` | 主控台範例，涵蓋所有功能 |

## 安裝

```
dotnet add package ThrottledLogging
```

## 用法

在任意 `ILogger` 上呼叫限流擴充方法。每次呼叫需傳入一個 **key**（用於識別被限流的日誌訊息）和一個 **interval**（兩次輸出之間的最短間隔）。

```csharp
// "disk-full" 這個 key 每分鐘最多輸出一次。
// 抑制後下次恢復輸出時，會自動附加"(N messages suppressed)"。
logger.LogWarningThrottled("disk-full", TimeSpan.FromMinutes(1), "Disk usage is above {Percent}%", usage);
```

可用方法與標準 `ILogger` API 一一對應：

- `LogTraceThrottled`
- `LogDebugThrottled`
- `LogInformationThrottled`
- `LogWarningThrottled`
- `LogErrorThrottled`
- `LogCriticalThrottled`

每個方法的簽章為 `(string key, TimeSpan interval, string? messageTemplate, params ReadOnlySpan<object?> args)`。每個方法還有一個在 messageTemplate 之前接受 `Exception?` 的多載，與標準 `ILogger` 的用法一致：

```csharp
logger.LogErrorThrottled("payment-failed", TimeSpan.FromMinutes(1), exception, "Payment failed for order {OrderId}", orderId);
```

### 抑制計數

當某則被限流的訊息在經歷一次或多次抑制後重新允許輸出時，抑制次數會自動附加到訊息末尾。附加的後綴已本地化，在 `zh-TW` 環境下輸出為：

```
Disk usage is above 95% (3則訊息已被隱藏)
```

如需固定語言而不是跟隨系統目前的地區設定自動判斷，可以設定 `ThrottledLogger.Language`：

```csharp
ThrottledLogger.Language = CultureInfo.GetCultureInfo("zh-Hant");
```

將其設回 `null`（預設值）即可恢復跟隨 `CultureInfo.CurrentUICulture` 自動判斷。

### 多個獨立 key

每個 key 擁有獨立的限流計數器，互不干擾：

```csharp
logger.LogErrorThrottled("service-a-down", TimeSpan.FromSeconds(5), "Service A is unreachable");
logger.LogErrorThrottled("service-b-down", TimeSpan.FromSeconds(5), "Service B is unreachable");
```

### 重設與查詢限流狀態

使用 `ResetThrottle` 可以手動清除某個 key 的限流狀態（下一次呼叫會被視為首次呼叫）；使用 `TryGetThrottledSuppressedCount` 可以在不寫日誌的情況下查詢某個 key 目前被抑制的次數：

```csharp
logger.ResetThrottle("disk-full");

if (logger.TryGetThrottledSuppressedCount("disk-full", out var suppressed))
{
    Console.WriteLine($"目前有 {suppressed} 則訊息被抑制");
}
```

### 全域設定

可在應用程式啟動時調整全域的過期時間和清理週期：

```csharp
// 閒置項目 30 分鐘後過期；每 15 分鐘執行一次清理。
ThrottledLogger.Configure(expiry: TimeSpan.FromMinutes(30), cleanupPeriod: TimeSpan.FromMinutes(15));
```

| 參數 | 預設值 | 說明 |
|---|---|---|
| `expiry` | 1 小時 | 閒置項目（無輸出也無被抑制的呼叫）在被移除前的保留時間。仍處於節流間隔內的項目不會被移除，因此應避免將超長間隔與數量無上限的 key 搭配使用。 |
| `cleanupPeriod` | 1 小時 | 背景清理計時器的執行間隔。必須不小於 1 毫秒且不超過約 49.7 天（4294967294 毫秒）；傳入 `Timeout.InfiniteTimeSpan` 可停用清理。 |

## 環境需求

.NET 10 或更新版本。

## 建置

```bash
dotnet build
dotnet test
dotnet pack
```

`dotnet test` 透過 [Microsoft.Testing.Platform](https://aka.ms/testingplatform)（MTP）執行，相關設定見 `global.json`。
