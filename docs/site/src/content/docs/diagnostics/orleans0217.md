---
title: "ORLEANS0217: 在任务取消后的清理调用中传入 AttemptCancellationToken"
description: 了解 ORLEANS0217，它报告任务取消后的清理代码将已经取消的 CancellationToken 传给调用的情况，并介绍如何使用 AttemptCancellationToken 或受取消屏蔽保护的步骤修复。
ms.date: 10/19/2026
ms.topic: reference
---

<a id="orleans0217-pass-attemptcancellationtoken-to-calls-in-task-cancellation-cleanup"></a>

# ORLEANS0217: 在任务取消后的清理调用中传入 AttemptCancellationToken

| 属性 | 值 |
| --- | --- |
| 严重性 | 警告 |
| 类别 | 用法 |
| 代码修复 | 可用 |

<a id="cause"></a>

## 原因

仅在任务取消后运行的代码将 `DurableTaskContext.CancellationToken` 传给调用。这类代码位于 `catch` 子句的处理程序、`if` 语句或条件表达式的真分支中，其筛选条件或判断条件为 `DurableTaskContext.IsTaskCancellationRequested`（单独出现或作为 `&&` 的操作数）：

:::code source="snippets/durable-tasks/Orleans0217.cs" id="orleans0217_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

持久化任务的上下文包含两个取消令牌：

- `CancellationToken` 在任务取消或执行尝试取消时都会被取消，因此普通任务代码发起的调用在这两种情况下都能及时结束。
- `AttemptCancellationToken` 仅在执行尝试取消时被取消，例如任务所属的激活实例停止时。

任务取消会与任务一起记录，因此在被取消的执行尝试及其后每次执行尝试中，`IsTaskCancellationRequested` 始终为 `true`，而 `CancellationToken` 始终处于已取消状态。因此，任务取消后执行的清理代码传入的是已取消的令牌，观察该令牌的调用会立即结束。在示例中，`ReleaseAsync` 在释放房间之前引发 `OperationCanceledException`，异常传播出任务主体，最终任务以已取消状态结束，但房间仍被占用。

此规则检查该代码区域本身。区域中的 Lambda 表达式和局部函数可能在其他任务的上下文中运行（例如步骤的上下文），因此规则会跳过它们。

有关这两个令牌，请参阅[任务取消与执行尝试取消](../grains/durable-tasks-structured-concurrency.md#task-cancellation-and-attempt-cancellation)；有关清理，请参阅[在工作流取消时执行内联补偿](../grains/durable-tasks-recipes.md#compensate-inline-when-the-workflow-is-canceled)。

<a id="how-to-fix-violations"></a>

## 如何修复违规

**传入 `AttemptCancellationToken`。** 代码修复将 `CancellationToken` 替换为 `AttemptCancellationToken`，并保留接收方表达式。调用能够运行至完成，同时在宿主取消执行尝试时仍会及时结束：

:::code source="snippets/durable-tasks/Orleans0217.cs" id="orleans0217_fix_attempt_token" language="csharp":::

**在受取消屏蔽保护的步骤中释放资源。** 步骤接收自己的取消令牌，任务取消不会传递到受屏蔽保护的步骤。步骤记录其结果，因此处理程序重放时会直接返回结果，只释放一次房间：

:::code source="snippets/durable-tasks/Orleans0217.cs" id="orleans0217_fix_shielded_step" language="csharp":::

进程内清理（例如释放信号量或释放资源）应放在 `finally` 块和 `using` 语句中，它们在两种取消情况下都会执行。

<a id="when-to-suppress"></a>

## 何时抑制诊断

如果调用在任务取消后本就应该立即结束，例如任务取消后清理过程应跳过的可选工作，则可抑制此诊断。
