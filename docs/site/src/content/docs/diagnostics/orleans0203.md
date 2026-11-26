---
title: "ORLEANS0203:在最后外部等待后更改 grain 状态"
description: "理解 ORLEANS0203, 其中报告一个步骤或身体尾部在外部等待之前改变状态, 并通过重新排序或记录更改来修正状态。"
ms.date: 10/15/2026
ms.topic: reference
---

<a id="orleans0203-change-grain-state-after-the-last-external-await"></a>

# ORLEANS0203:在最后外部等待后更改 grain 状态

| 财产 | 价值值值 |
| --- | --- |
| 严重性 | 警告 |
| 类别类 | 用法 |
| 代码修正 | 无无无无 |

<a id="cause"></a>

## 原因原因原因

代码运行一次, 如本地步或身体同步尾部等, 更改 grain 状态,然后等待外部操作后再返回。更改是指任务、递增或一个 grain 成员国,例如 `IDurableValue<T>.Value` 或字典或列表元素,或更改收藏的调用,例如 `Add`, `AddRange`, `Remove`, `Clear`, `Insert`, `RemoveAt`, `Enqueue`, `Dequeue`, `TryDequeue`, `TryAdd`, `UnionWith`, `IntersectWith`, `ExceptWith`, `SymmetricExceptWith`, `IDurableLog<T>.Append` 和 `TrimBefore`, 和 `TrySet` 方法的方法和方法问题 `IDurableTaskCompletionSource<T>`,和 `IPersistentState<TState>.WriteStateAsync` 和 `ClearStateAsync`。以下步骤计为通知,然后发送:

:::code source="snippets/durable-tasks/Orleans0203.cs" id="orleans0203_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

步骤结果在运行其最终同步代码的工作项目中进行, 因此该代码在同一日记中承诺的状态变化会因此被写入。 在外部等待之前的更改会提前进行 。 虽然该步骤正在等待, 其他关于 grain的工作, 如另一个工作流程步骤或普通 grain 方法, 可以要求写入一个日记, 而写时不会出现步骤结果, 并且该命令会自动执行修改 。

当运行在结果发生前结束时, 例如由于激活停止操作, 下一运行再次运行该步骤: 它第二次计算通知并发送电子邮件。 任务记录显示一个通知, 而 grain状态则显示两个 。

承诺模式见 [重演安全规则](../grains/durable-tasks-replay-rules.md)。

<a id="how-to-fix-violations"></a>

## 如何纠正违反情况

**更改状态是在最后外部等待之后。** 该变化是该步骤最终同步代码的一部分,并承诺结果如下:

:::code source="snippets/durable-tasks/Orleans0203.cs" id="orleans0203_fix_reorder" language="csharp":::

**分别记录每个变化。** 将工作分为外部呼叫的一个步骤和状态变化的 `DurableTask.RunInline` 效应。每种效果都以自己的记录承诺更改:

:::code source="snippets/durable-tasks/Orleans0203.cs" id="orleans0203_fix_split" language="csharp":::

<a id="when-to-suppress"></a>

## 何时压缩

抑制对一非能力变化的诊断,例如设定每运行一次结果都设定相同结果的价值,如果在外部呼叫可接受之前就承诺了。
