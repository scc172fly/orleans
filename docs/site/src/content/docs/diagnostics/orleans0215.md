---
title: "ORLEANS0215: 通过重放安全的日志记录器记录日志"
description: 了解 ORLEANS0215，它报告持久化任务中会在重放时再次执行的日志调用，并介绍如何使用 ReplaySafe() 修复。
ms.date: 10/15/2026
ms.topic: reference
---

<a id="orleans0215-log-through-a-replay-safe-logger"></a>

# ORLEANS0215: 通过重放安全的日志记录器记录日志

| 属性 | 值 |
| --- | --- |
| 严重性 | 信息 |
| 类别 | 用法 |
| 代码修复 | 无 |

<a id="cause"></a>

## 原因

持久化任务主体在后续仍会到达持久化操作的位置，通过 `ILogger` 使用 `LoggerExtensions` 方法、`ILogger.Log` 或 `[LoggerMessage]` 方法记录日志，且该日志记录器并非由 `ReplaySafe()` 返回：

:::code source="snippets/durable-tasks/Orleans0215.cs" id="orleans0215_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

重放会再次执行任务主体最后一个持久化操作之前的代码，包括日志调用。示例中的导出任务在处理第十个分区时停用后，重放会为已完成的九个分区再次记录 `Exporting partition`，每次重放都会重复这些日志。这样，日志夸大了实际完成的工作量，基于日志的指标和警报也会重复计数。

`ReplaySafe()` 返回的日志记录器会在任务重放期间跳过日志事件：从发现已有操作记录的本次运行开始，到该次运行遇到此前任何一次运行都未记录的操作为止。这样，每个事件由首次到达它的运行记录。如果激活实例在该次运行的写入提交之前结束，下次运行会重复未提交的工作及其日志。因此，这些日志适用于诊断，而要求恰好一次的指标或审计信息应放在已记录的结果中。

有关重放安全的日志记录，请参阅[重放与确定性](../grains/durable-tasks.md#replay-and-determinism)。

<a id="how-to-fix-violations"></a>

## 如何修复违规

使用 `ReplaySafe()` 包装日志记录器，通常在 Grain 构造函数中执行一次即可。日志记录器在记录日志时读取当前运行的任务，因此普通 Grain 方法也可以使用它：

:::code source="snippets/durable-tasks/Orleans0215.cs" id="orleans0215_fix" language="csharp":::

`DurableTask.RunInline` 效果、步骤或任务主体同步尾部中的日志，只会在相应结果提交之前重复：重放直接返回已记录的结果，不执行其代码；若激活实例结束前记录未提交，则相关代码和日志调用会再次执行。

<a id="when-to-suppress"></a>

## 何时抑制诊断

如果日志应记录每次运行，例如用于显示重放发生位置的调试日志，则可抑制此诊断。
