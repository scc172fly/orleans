---
title: "ORLEANS0226: 使持久化任务主体在其调度器上继续执行"
description: 了解 ORLEANS0226，它报告导致持久化任务主体的延续操作离开其 Grain 调度器的 ConfigureAwait 调用，并介绍通过不使用 ConfigureAwait 直接等待来修复。
ms.date: 10/19/2026
ms.topic: reference
---

<a id="orleans0226-continue-durable-task-bodies-on-their-scheduler"></a>

# ORLEANS0226: 使持久化任务主体在其调度器上继续执行

| 属性 | 值 |
| --- | --- |
| 严重性 | 错误 |
| 类别 | 用法 |
| 代码修复 | 无 |

<a id="cause"></a>

## 原因

持久化任务主体（包括步骤）使用 `ConfigureAwait(false)` 等待，或使用未设置 `ConfigureAwaitOptions` 枚举中 `ContinueOnCapturedContext` 标志的方式等待：

:::code source="snippets/durable-tasks/Orleans0226.cs" id="orleans0226_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

持久化任务主体在其 Grain 激活实例的调度器上运行，该调度器一次只运行一段 Grain 代码。调度器对主体的持久化操作和状态变更排序，运行时将主体的结果与主体尾部所做的状态变更一起提交。

如果等待操作在不使用所捕获上下文的情况下继续执行，则主体会在线程池上恢复：

- 持久化操作（例如 `DurableTask.RunInline`、任务启动及 `DurableTask.GetVersion`）会在那里抛出 `InvalidOperationException`，因为执行实例在自己的调度器上对操作排序并记录操作。
- 状态变更会与 Grain 的其他执行轮次发生竞态，包括 Grain 方法及其他执行实例。
- 尾部的状态变更与主体的结果分别提交。在示例中，`_lastRate` 提交后、结果提交前发生崩溃，会导致尾部再次运行，从而再次获取汇率并为 `_lastRate` 赋值。

ORLEANS0014 报告一般 Grain 代码中的 `ConfigureAwait(false)`。

有关主体尾部，请参阅[重放代码、已记录操作与尾部](../grains/durable-tasks-replay-rules.md#replayed-code-recorded-operations-and-the-tail)。

<a id="how-to-fix-violations"></a>

## 如何修复违规

**不使用 `ConfigureAwait`，直接等待。** 延续操作返回激活实例的调度器。`ConfigureAwait(true)` 和 `ConfigureAwaitOptions.ContinueOnCapturedContext` 也会在那里继续执行：

:::code source="snippets/durable-tasks/Orleans0226.cs" id="orleans0226_fix" language="csharp":::

主体等待的库方法保留其自身的 `ConfigureAwait(false)`：此规则检查主体中的等待操作，而主体对库方法的等待会在调度器上恢复。

<a id="when-to-suppress"></a>

## 何时抑制诊断

请保持此诊断启用：持久化操作在调度器之外会抛出异常，而那里的状态变更会与 Grain 的其他工作发生竞态。
