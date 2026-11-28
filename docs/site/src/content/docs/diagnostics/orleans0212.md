---
title: "ORLEANS0212: 使用 IsTaskCancellationRequested 筛选取消处理程序"
description: 了解 ORLEANS0212：处理程序捕获持久化操作的取消异常时未使用 DurableTask.Current.IsTaskCancellationRequested 筛选。使用筛选器或重新抛出来修复。
ms.date: 10/19/2026
ms.topic: reference
---

<a id="orleans0212-filter-cancellation-handlers-by-istaskcancellationrequested"></a>

# ORLEANS0212: 使用 IsTaskCancellationRequested 筛选取消处理程序

| 属性 | 值 |
| --- | --- |
| 严重性 | 警告 |
| 类别 | 用法 |
| 代码修复 | 无 |

<a id="cause"></a>

## 原因

持久化任务主体中的 `try` 块包含持久化操作，其中某个 `catch` 子句捕获 `OperationCanceledException`、其基类型或派生类型，且既没有读取 `DurableTaskContext.IsTaskCancellationRequested` 的筛选器，也不重新抛出异常：

:::code source="snippets/durable-tasks/Orleans0212.cs" id="orleans0212_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

持久化任务通过 `OperationCanceledException` 观察两类取消：

- **任务取消**是持久化的：调用方请求取消，例如对任务句柄使用 `CancelAsync`，宿主将取消与任务一同记录，并记录每次执行首次观察到取消的位置。重放在同一位置观察取消，主体决定任务结果，例如执行补偿。
- **执行尝试取消**停止主体的当前运行，例如激活实例停止，或者任务被中止或删除。该执行尝试后续的持久化操作抛出 `DurableTaskAttemptCanceledException`，其日志化状态变更随 Grain 的下一次日志写入提交。停止的激活实例中的任务会在新的执行尝试中继续执行。

不带筛选的处理程序会将两者一视同仁。当宿主在预订保留期间取消本次执行尝试时，示例中的处理程序会对无人取消的预订执行补偿：它的状态变更被提交，其外部调用（例如在预订系统中释放房间）产生效果，而下一次执行尝试会重放任务并确认预订。

`DurableTask.Current.IsTaskCancellationRequested` 仅在任务取消时为 `true`，无论是在被取消的执行尝试中，还是之后的每次执行尝试中。

有关补偿，请参阅[工作流取消时内联补偿](../grains/durable-tasks-recipes.md#compensate-inline-when-the-workflow-is-canceled)。

<a id="how-to-fix-violations"></a>

## 如何修复违规

**筛选处理程序。** 这样处理程序仅在任务取消时运行，执行尝试取消的异常则结束当前运行。用 `Shielded()` 屏蔽补偿子任务，使其即使在任务已请求取消时仍能运行：

:::code source="snippets/durable-tasks/Orleans0212.cs" id="orleans0212_fix_filter" language="csharp":::

**补偿后重新抛出。** 以 `throw;` 结束的处理程序使任何取消都结束本次运行，并在任务取消之后使任务以已取消状态结束：

:::code source="snippets/durable-tasks/Orleans0212.cs" id="orleans0212_fix_rethrow" language="csharp":::

<a id="when-to-suppress"></a>

## 何时抑制

如果处理程序代码对任务取消和执行尝试取消均正确，可以抑制诊断，例如只通过重放安全的日志记录器记录日志。
