---
title: "ORLEANS0200:记录效果或最后一次耐用操作后,进入 grain 状态"
description: "理解 ORLEANS0200, 其中报告持久任务代码, 读取或修改 grain 的代码, 说明重播再次运行的位置, 用记录的效果、同步尾巴或本地步骤来修正它 。"
ms.date: 10/15/2026
ms.topic: reference
---

<a id="orleans0200-access-grain-state-in-recorded-effects-or-after-the-last-durable-operation"></a>

# ORLEANS0200:记录效果或最后一次耐用操作后,进入 grain 状态

| 财产 | 价值值值 |
| --- | --- |
| 严重性 | 警告 |
| 类别类 | 用法 |
| 代码修正 | 无无无无 |

<a id="cause"></a>

## 原因原因原因

持久任务机构读取或更改其仍然能够持续运行的点 grain 状态。 Grain 状态是一个田地或财产,其类型是或执行一种已日记收藏、 `IDurableValue<T>`、 `IDurableDictionary<TKey, TValue>`、 `IDurableList<T>`、 `IDurableQueue<T>`、 `IDurableSet<T>`、 `IDurableTaskCompletionSource<T>`或 `IDurableLog<T>`,或国家持续存在, `IPersistentState<TState>`。

:::code source="snippets/durable-tasks/Orleans0200.cs" id="orleans0200_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

重播从一开始就运行身体, 而每个记录的行动都返回了记录的结果。 Grain 状态没有重建: 它持有所有承诺的改变, 包括后来操作所做的修改。 因此, 在机构最后一次持久操作之前的代码再次与新国家对抗 :

- **重新应用写作。** 停止使用后, `RemindAsync` 例加加加加 `_remindersSent` 第二次,尽管《古兰经》曾被派遣一次。
- **阅读后会观察写作。** 重放 `ShipWhenPaidAsync` 原文如下: `Shipped`记录,其中 `ship` 步骤写入, 跳过第一个运行的分支。 重放会显示不同的操作或相同操作, 或带有不同参数的相同的操作, 返回不同的结果或差异并阻断执行 。

三次访问 grain 状态一次:

- `DurableTask.RunInline` 效果运行一次,其状态变化随记录而承诺。
- 当地一步, `async DurableTask` 方法的相同 grain,承诺对其最终代码进行州级修改并取得记录结果。
- 身体的同步尾巴, 代码,在最后一次持久操作后, 承诺改变状态 与任务的结果。

规则和重放模型见 [重演安全规则](../grains/durable-tasks-replay-rules.md)。

<a id="how-to-fix-violations"></a>

## 如何纠正违反情况

**记录访问 `DurableTask.RunInline`.** 效果运行一次并返回其输出, 重放返回记录值。 读取状态以同样的方式决定后期操作, 然后将进度附加到 `IDurableLog<T>` 与 `DurableTask.RunInline(() => log.Append(entry))`,因此每个条目都附后一次:

:::code source="snippets/durable-tasks/Orleans0200.cs" id="orleans0200_fix_once" language="csharp":::

**将更改移动到同步尾部。** 修改若在任务结果完成后没有持久操作,则更改:

:::code source="snippets/durable-tasks/Orleans0200.cs" id="orleans0200_fix_tail" language="csharp":::

**将更改改变为本地步骤。** 一个其身体改变状态和返回的阶梯,如上面 `MarkShipped` ,会以记录的结果来实施修改,而重放结果则不运行该阶梯:

:::code source="snippets/durable-tasks/Orleans0200.cs" id="orleans0200_fix_step" language="csharp":::

记录效果见 [记录的效果、当地步骤和调用其他 grains](../grains/durable-tasks.md#recorded-effects-local-steps-and-calls-to-other-grains)。

<a id="when-to-suppress"></a>

## 何时压缩

禁止用于读取其价值永远无法决定持久操作、其参数或状态变化的诊断, 如仅达到日志消息的值, 以及任务运行时没有改变的状态, 例如 grain 在创建时写过一次数值 。
