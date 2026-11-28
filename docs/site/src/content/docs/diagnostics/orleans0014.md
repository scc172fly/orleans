---
title: "ORLEANS0014: 保留 Grain 执行上下文"
description: 了解并解决 ConfigureAwait 可能使延续操作移出 Grain 执行上下文时出现的 ORLEANS0014。
ms.date: 08/25/2026
ms.topic: reference
---

<a id="orleans0014-preserve-the-grain-execution-context"></a>

# ORLEANS0014: 保留 Grain 执行上下文

| 属性 | 值 |
| --- | --- |
| 类别 | 用法 |
| 严重性 | 警告 |
| 代码修复 | 可用 |

<a id="cause"></a>

## 原因

Grain 或系统目标的方法使用 `ConfigureAwait(false)`，或者使用 `ConfigureAwaitOptions` 而未包含 `ContinueOnCapturedContext`。

<a id="impact"></a>

## 影响

延续操作可能在 Grain 调度器之外执行，并丢失激活实例的执行上下文。await 之后的代码可能因此违反 Orleans 基于执行轮次的并发保证，或观察到错误的 Grain 上下文。

<a id="how-to-fix"></a>

## 修复方法

优先使用普通的 `await`。必须使用 `ConfigureAwait` 时，使用 `ConfigureAwait(true)` 或包含 `ConfigureAwaitOptions.ContinueOnCapturedContext`。

代码修复会将 `false` 改为 `true`、替换 `ConfigureAwaitOptions.None`，或者向现有选项添加 `ContinueOnCapturedContext`。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

仅在完整的延续操作被明确设计为不依赖上下文，且无法访问 Grain 或运行时状态时抑制此诊断。抑制之前请检查 await 之后的完整调用路径。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0014.severity = none
```
