---
title: "ORLEANS0217:通过 AttemptCancellationToken 电话来清理任务取消的清除"
description: "理解 ORLEANS0217, 其中报告任务取消后清理工作, 将已经排出 CancellationToken 的调用通过一个调用, 并用 AttemptCancellationToken 或屏蔽步骤来修正它。"
ms.date: 10/19/2026
ms.topic: reference
---

<a id="orleans0217-pass-attemptcancellationtoken-to-calls-in-task-cancellation-cleanup"></a>

# ORLEANS0217:通过 AttemptCancellationToken 电话来清理任务取消的清除

| 财产 | 价值值值 |
| --- | --- |
| 严重性 | 警告 |
| 类别类 | 用法 |
| 代码修正 | 可用 |

<a id="cause"></a>

## 原因原因原因

仅在任务取消后运行的代码通过 `DurableTaskContext.CancellationToken` 呼叫。该代码是 `catch` 条款或 `if` 语句或有条件表达的真正分支的处理者,其过滤器或条件为 `DurableTaskContext.IsTaskCancellationRequested`,单独或作为 `&&`的操作:

:::code source="snippets/durable-tasks/Orleans0217.cs" id="orleans0217_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

长期任务的背景包含两个取消标记:

- `CancellationToken` 被取消任务和尝试取消取消,因此普通任务代码在这两种情况下迅速终止的电话。
- `AttemptCancellationToken` 仅通过尝试取消来取消, 例如当任务启动停止时 。

任务取消会随任务记录, 所以 `IsTaskCancellationRequested` 将保留 `true`, `CancellationToken` 也将在取消的尝试中和以后每一次尝试中继续取消。 在任务取消后进行的清理会传递一个取消的牌子, 以及一个同时结束观察的电话。 例如, `ReleaseAsync` 在释放房间之前扔出 `OperationCanceledException` , 例外就会脱离身体, 而任务结束时仍然保持着房间。

规则检查了区域本身的代码。 区域中的兰巴兹和当地功能可以在另一个任务背景下运行, 比如一步, 因此规则可以跳过它们 。

两只牌子,见 [任务取消和任务取消](../grains/durable-tasks-structured-concurrency.md#task-cancellation-and-attempt-cancellation),清理见 [当工作流程取消时进行内线补偿](../grains/durable-tasks-recipes.md#compensate-inline-when-the-workflow-is-canceled)。

<a id="how-to-fix-violations"></a>

## 如何纠正违反情况

**通行证通过 `AttemptCancellationToken`.** 代码修正替换 `CancellationToken` 与 `AttemptCancellationToken` 并保留接收器。 呼叫会运行到完成, 当主机取消尝试时仍然会迅速结束:

:::code source="snippets/durable-tasks/Orleans0217.cs" id="orleans0217_fix_attempt_token" language="csharp":::

**以屏蔽的步数释放。** 一步收到自己的取消标记, 而任务取消没有达到屏蔽的步数。 步骤记录了结果, 因此一个处理器的重放返回它并一次性释放房间:

:::code source="snippets/durable-tasks/Orleans0217.cs" id="orleans0217_fix_shielded_step" language="csharp":::

流程中的清理,如释放一个血肿或处置资源,属于 `finally` 区块和 `using` 语句中,这两种语句都适用取消。

<a id="when-to-suppress"></a>

## 何时压缩

任务取消后, 终止该通话的诊断, 例如任务取消后清理会跳过的任选工作 。
