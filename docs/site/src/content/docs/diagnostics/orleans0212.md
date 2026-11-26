---
title: "ORLEANS0212: IsTaskCancellationRequested中的过滤器取消处理器"
description: "理解 ORLEANS0212, 该操作报告处理器在不通过 DurableTask.Current.IsTaskCancellationRequested过滤的情况下,在耐用操作周围捕获取消的处理器, 并使用过滤器或重新丢转来修正它。"
ms.date: 10/19/2026
ms.topic: reference
---

<a id="orleans0212-filter-cancellation-handlers-by-istaskcancellationrequested"></a>

# ORLEANS0212: IsTaskCancellationRequested中的过滤器取消处理器

| 财产 | 价值值值 |
| --- | --- |
| 严重性 | 警告 |
| 类别类 | 用法 |
| 代码修正 | 无无无无 |

<a id="cause"></a>

## 原因原因原因

A `try` 在一个持久任务机构内,一个长期任务机构有耐久的业务,其 `catch` 条款的渔获量 `OperationCanceledException`,它从某类型或从该类型产生的类型没有过滤器,且 `DurableTaskContext.IsTaskCancellationRequested`并且不会再次发射:

:::code source="snippets/durable-tasks/Orleans0212.cs" id="orleans0212_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

一项持久任务通过 `OperationCanceledException`观察到两种取消:

- **任务取消** 是持久的:一个打电话者要求它,例如对任务处理的 `CancelAsync` ,主机记录任务,以及每次执行时首先观察的任务。重播在同一点观察任务,而机构决定任务结果,例如通过补偿。
- **取消企图** 停止正文当前运行, 例如激活停止时停止, 或者任务中止或删除时停止。 此尝试的后期持久操作会扔弃 `DurableTaskAttemptCanceledException`,以及它更改了日记状态 grain下一期日记的写。停止激活的任务将再次继续。

未经过滤的处理器对两者一视同仁。 当主机在搁置期内取消尝试时, 示例中的处理器可以补偿一个没有人取消的预订: 它的状态改变承诺, 以及它发出的外部呼叫( 如将房间放入订票系统) 生效, 而下一次尝试重播任务并确认预定 。

`DurableTask.Current.IsTaskCancellationRequested` 是 `true` 仅用于取消任务,在取消尝试中和以后每一次尝试中。

赔偿,见 [当工作流程取消时进行内线补偿](../grains/durable-tasks-recipes.md#compensate-inline-when-the-workflow-is-canceled)。

<a id="how-to-fix-violations"></a>

## 如何纠正违反情况

**过滤处理器。** 操作者然后跑去取消任务,但尝试取消的例外情况结束。 `Shielded()`,因此它们运行,尽管任务取消是请求:

:::code source="snippets/durable-tasks/Orleans0212.cs" id="orleans0212_fix_filter" language="csharp":::

**补偿后重新进行重写。** 一个处理器, 以 `throw;` 结束, 使每个取消的操作都结束运行, 任务在任务取消后终止 :

:::code source="snippets/durable-tasks/Orleans0212.cs" id="orleans0212_fix_rethrow" language="csharp":::

<a id="when-to-suppress"></a>

## 何时压缩

当处理器代码对任务取消和尝试取消来说正确时, 即禁止诊断, 例如一个处理器只通过重放安全记录器记录日志条目 。
