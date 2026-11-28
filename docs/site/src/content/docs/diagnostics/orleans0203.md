---
title: "ORLEANS0203: 在最后一次外部等待之后修改 Grain 状态"
description: 了解 ORLEANS0203：步骤或主体尾部在外部等待之前修改状态。通过调整顺序或记录变更来修复。
ms.date: 10/15/2026
ms.topic: reference
---

<a id="orleans0203-change-grain-state-after-the-last-external-await"></a>

# ORLEANS0203: 在最后一次外部等待之后修改 Grain 状态

| 属性 | 值 |
| --- | --- |
| 严重性 | 警告 |
| 类别 | 用法 |
| 代码修复 | 无 |

<a id="cause"></a>

## 原因

本地步骤或主体同步尾部等只运行一次的代码，修改 Grain 状态后，在返回之前等待外部操作。变更包括对 Grain 状态成员（例如 `IDurableValue<T>.Value` 或字典、列表元素）的赋值、递增、递减，或修改集合的调用，例如 `Add`、`AddRange`、`Remove`、`Clear`、`Insert`、`RemoveAt`、`Enqueue`、`Dequeue`、`TryDequeue`、`TryAdd`、`UnionWith`、`IntersectWith`、`ExceptWith`、`SymmetricExceptWith`、`IDurableLog<T>.Append` 和 `TrimBefore`，`TrySet` 方法（属于 `IDurableTaskCompletionSource<T>`），以及 `IPersistentState<TState>.WriteStateAsync` 和 `ClearStateAsync`。以下步骤先统计一次通知，再发送它：

:::code source="snippets/durable-tasks/Orleans0203.cs" id="orleans0203_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

步骤的结果在执行其最后一段同步代码的工作项中暂存，因此该代码所作的状态变更与结果在同一次日志写入中提交。位于外部等待之前的变更会更早暂存。在步骤等待期间，Grain 上的其他工作（例如另一个工作流的步骤或普通 Grain 方法）可以请求日志写入，此次写入会提交该变更，却不提交此步骤的结果。

若运行随后在结果提交前结束，例如激活实例停用，则下一次运行会再次执行该步骤：第二次统计通知，并再次发送邮件。任务记录显示一次通知，而 Grain 状态显示两次。

有关提交模型，请参阅[重放安全规则](../grains/durable-tasks-replay-rules.md)。

<a id="how-to-fix-violations"></a>

## 如何修复违规

**在最后一次外部等待之后修改状态。** 此变更便属于步骤的最后一段同步代码，并与其结果一同提交：

:::code source="snippets/durable-tasks/Orleans0203.cs" id="orleans0203_fix_reorder" language="csharp":::

**分别记录各次变更。** 将工作拆分为用于外部调用的步骤，以及用于状态变更的 `DurableTask.RunInline` 效果。每个效果将其变更与自己的记录一同提交：

:::code source="snippets/durable-tasks/Orleans0203.cs" id="orleans0203_fix_split" language="csharp":::

<a id="when-to-suppress"></a>

## 何时抑制

当变更是幂等的（例如每次运行都将值设为相同结果），并且允许它在外部调用之前提交时，可以抑制此诊断。
