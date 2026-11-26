---
title: "ORLEANS0223:将新值指定到 grain 状态,而不是将其修改到位"
description: "理解 ORLEANS0223, 其中报告持久任务代码, 该代码会改变日记 grain 状态所保存的对象, 用代码指定的不可变值来修正它 。"
ms.date: 10/19/2026
ms.topic: reference
---

<a id="orleans0223-assign-new-values-to-grain-state-instead-of-changing-them-in-place"></a>

# ORLEANS0223:将新值指定到 grain 状态,而不是将其修改到位

| 财产 | 价值值值 |
| --- | --- |
| 严重性 | 警告 |
| 类别类 | 用法 |
| 代码修正 | 无无无无 |

<a id="cause"></a>

## 原因原因原因

永久任务代码 grain 状态,即:指定、加增或降级成员或对象的一个元素,或要求改变集合的方法,例如 `Add` 或 `Clear`对象。 `IDurableValue<T>.Value`, `IPersistentState<T>.State`,或直接或通过持有下列内容的当地人输入耐久字典或清单:

:::code source="snippets/durable-tasks/Orleans0223.cs" id="orleans0223_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

耐久值和收藏记录通过它们的API的每项变化,例如指定给 `IDurableValue<T>.Value` 或一个长词典条目。修改承诺在 grain下一本日记中写字。

更改这些操作跟踪的值或收藏条目所持有的对象时, 其位置会绕过 API。 内存中的对象会维持该变化, 直到另一个修改写入整个值后才保存它。 例如, 任务完成并实现结果, 而日记则持有愿望列表而不保留新项目。 从日记中回收 grain 的激活会丢失此项 。

以日记为主的 `IPersistentState<T>` 将整个状态记录在每本期刊的写作上,包括对其序列成员和收藏进行现场修改。 该规则还建议在耐久任务代码中为该状态提供不可改变的价值和分配。

价值类型、成员一无所有的记录和不可改变的收款因任务而变化, API 记录了这些变化,因此规则没有报告其中的任何内容。

重播安全状态见 [重演安全规则](../grains/durable-tasks-replay-rules.md#state-values-and-external-work)。

<a id="how-to-fix-violations"></a>

## 如何纠正违反情况

**存储不可变值,并指定一个新的值。** 含有不可变收藏的记录返回了已更改的副本,任务记录则记录如下:

:::code source="snippets/durable-tasks/Orleans0223.cs" id="orleans0223_fix" language="csharp":::

<a id="when-to-suppress"></a>

## 何时压缩

国家只保留在记忆中的成员,例如序列器跳过的缓存,禁止其诊断。
