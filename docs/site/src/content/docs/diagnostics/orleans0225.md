---
title: "ORLEANS0225: 在持久化任务主体中等待持久化任务，而不是轮询"
description: 了解 ORLEANS0225，它报告在持久化任务主体中轮询已调度任务的行为，此类行为会引发异常。可以通过等待任务或使用持久化超时等待进行修复。
ms.date: 10/19/2026
ms.topic: reference
---

<a id="orleans0225-await-durable-tasks-instead-of-polling-them-in-durable-task-bodies"></a>

# ORLEANS0225: 在持久化任务主体中等待持久化任务，而不是轮询

| 属性 | 值 |
| --- | --- |
| 严重性 | 错误 |
| 类别 | 用法 |
| 代码修复 | 无 |

<a id="cause"></a>

## 原因

持久化任务主体（包括步骤）使用 `ScheduledTask.IsCompletedAsync`、`ScheduledTask.GetStatusAsync` 或 `ScheduledTask.GetResponseAsync`（带有 `PollingOptions`）轮询已调度任务：

:::code source="snippets/durable-tasks/Orleans0225.cs" id="orleans0225_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

轮询返回轮询执行时的任务状态，而执行实例不会为其记录任何信息。重放在另一个时间重新运行主体，此时轮询可能观察到不同的状态。根据状态进行分支判断的主体（例如示例中的循环）因而会调度不同于执行实例此前记录的那次运行的操作，导致重放发生偏离。

在持久化任务内部进行轮询时，运行时会抛出 `NotSupportedException`，因此主体在首次轮询时便会失败。

等待操作或带持久化超时的等待会记录其结果，每次重放都会观察到相同的结果。`ScheduledTask.GetResponseAsync` 在不带 `PollingOptions` 时与直接等待一样，会等待终止响应。持久化任务主体外的代码（例如客户端）可以轮询。

有关句柄和超时，请参阅[组合器与句柄](../grains/durable-tasks.md#combinators-and-handles)。

<a id="how-to-fix-violations"></a>

## 如何修复违规

**等待任务。** 任务完成时主体恢复执行，重放返回已记录的结果。如果主体需要先完成其他工作，可以先调度任务，再等待其句柄：

:::code source="snippets/durable-tasks/Orleans0225.cs" id="orleans0225_fix_await" language="csharp":::

**使用持久化超时等待。** 带有超时的 `WaitAsync` 使任务与持久化延迟竞争。如果延迟先完成，任务会收到取消请求；如果任务最终以已取消状态结束，则会抛出 `TimeoutException`。句柄的 `WaitAsync` 带有超时时也以相同方式等待；`ScheduledTask.WhenAny` 与已调度的 `DurableTask.Delay` 组合使用时返回先完成的操作：

:::code source="snippets/durable-tasks/Orleans0225.cs" id="orleans0225_fix_timeout" language="csharp":::

<a id="when-to-suppress"></a>

## 何时抑制诊断

请保持此诊断启用：在持久化任务主体中轮询会抛出 `NotSupportedException`。轮询应在持久化任务主体之外的代码中进行，例如客户端或普通 Grain 方法。
