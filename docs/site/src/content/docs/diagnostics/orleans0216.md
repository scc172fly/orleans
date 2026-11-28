---
title: "ORLEANS0216: 从 Grain 或持久化任务主体投递持久化任务"
description: 了解 ORLEANS0216，它报告在没有持久化发件箱可暂存启动操作的位置投递持久化任务的代码，并介绍如何使用 ScheduleAsync 或从持久化 Grain 投递来修复。
ms.date: 10/15/2026
ms.topic: reference
---

<a id="orleans0216-post-durable-tasks-from-grains-or-durable-task-bodies"></a>

# ORLEANS0216: 从 Grain 或持久化任务主体投递持久化任务

| 属性 | 值 |
| --- | --- |
| 严重性 | 警告 |
| 类别 | 用法 |
| 代码修复 | 无 |

<a id="cause"></a>

## 原因

Grain 和持久化任务主体之外的代码（例如客户端或 Web 终结点）对持久化任务定义或配置调用 `Post`：

:::code source="snippets/durable-tasks/Orleans0216.cs" id="orleans0216_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

`Post` 是同步操作：它将启动操作暂存到执行调用代码的 Grain 的持久化发件箱中，并在该 Grain 下次写入日志时，与调用代码所做的状态更改一起提交。Grain 方法、持久化任务主体和 `DurableTask.Run` 委托都在 Grain 中运行。客户端没有持久化发件箱，因此 `Post` 在客户端会引发 `InvalidOperationException`，导致结账失败。

有关任务如何启动，请参阅[启动和观察任务](../grains/durable-tasks.md#starting-and-observing-tasks)。

<a id="how-to-fix-violations"></a>

## 如何修复违规

**使用 `ScheduleAsync` 调度任务。** 该方法在任务所属 Grain 接受并存储任务后完成，并返回任务句柄。使用 `WithId` 为任务命名，使超时等结果不明确的故障之后的重试仍指向同一个任务：

:::code source="snippets/durable-tasks/Orleans0216.cs" id="orleans0216_fix_schedule" language="csharp":::

**从持久化 Grain 投递。** 持久化 Grain 的普通方法投递启动操作，该操作在下次写入日志时与 Grain 状态更改一起原子提交：

:::code source="snippets/durable-tasks/Orleans0216.cs" id="orleans0216_fix_grain" language="csharp":::

<a id="when-to-suppress"></a>

## 何时抑制诊断

如果辅助方法仅由 Grain 或持久化任务主体调用，例如工作流用于启动后续工作的静态方法，则可抑制此诊断。
