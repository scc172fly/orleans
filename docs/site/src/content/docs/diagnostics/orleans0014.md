---
title: "ORLEANS0014:保留 grain 执行环境"
description: "理解和决心 ORLEANS0014 何时 ConfigureAwait 可在 grain 执行。"
ms.date: 08/25/2026
ms.topic: reference
---

<a id="orleans0014-preserve-the-grain-execution-context"></a>

# ORLEANS0014:保留 grain 执行环境

| 财产 | 价值值值 |
| --- | --- |
| 类别类 | 用法 |
| 严重性 | 警告 |
| 代码修正 | 可用 |

<a id="cause"></a>

## 原因原因原因

grain 或系统目标方法使用 `ConfigureAwait(false)`,或使用 `ConfigureAwaitOptions` 而不使用 `ContinueOnCapturedContext`。

<a id="impact"></a>

## 影响影响

继续可以在 grain 排程器之外执行, 并失去激活的执行环境。 等待之后的代码会违反 Orleans的转折货币担保或遵守错误的 grain 背景。

<a id="how-to-fix"></a>

## 如何修补

偏向于普通 `await`。当需要 `ConfigureAwait` 时,使用 `ConfigureAwait(true)` 或包括 `ConfigureAwaitOptions.ContinueOnCapturedContext`。

代码将修改 `false` 改为 `true`,替换 `ConfigureAwaitOptions.None`,或在现有选项中添加 `ContinueOnCapturedContext` 。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

只有在完全继续是故意的无背景状态且无法访问 grain 或运行时状态的情况下,才能抑制这一诊断。 在取消之前先审查等待后的全部路径 。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0014.severity = none
```
