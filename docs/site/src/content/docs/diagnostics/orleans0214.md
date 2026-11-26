---
title: "ORLEANS0214:为每次重复等待生成一个信号名称"
description: "理解 ORLEANS0214, 它报告在环中等待一个耐久信号名称, 并用 DurableSignal<T>来修正它。 for."
ms.date: 10/15/2026
ms.topic: reference
---

<a id="orleans0214-derive-a-signal-name-for-each-repeated-wait"></a>

# ORLEANS0214:为每次重复等待生成一个信号名称

| 财产 | 价值值值 |
| --- | --- |
| 严重性 | 信息信息 |
| 类别类 | 用法 |
| 代码修正 | 无无无无 |

<a id="cause"></a>

## 原因原因原因

持久任务机构电话 `WaitAsync` 或 `TryWaitAsync` 上a `DurableSignal<T>` 内部环循环,而信号不是由 `For`:

:::code source="snippets/durable-tasks/Orleans0214.cs" id="orleans0214_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

任务记录发送者为每个信号名称交付的第一个值, 并返回给每次等待该名称时的数值, 包括重放中的等待。 当同一名称的后一个值不同时, 拒绝作为冲突出现, 并在相同时作为重复报告 。

举例来说,第一次审查否决了合同。 每轮的等待都同时回响了拒绝的结果,因此谈判在一次审查之后结束,而审议员后来的决定则被否决。

信号见 [信号](../grains/durable-tasks.md#signals)。

<a id="how-to-fix-violations"></a>

## 如何纠正违反情况

以 `DurableSignal<T>.For`, 从一个能识别其值(如圆数)中, 产生每个事件的名称。 发件人将每个决定传送到同一发源信号, 如 `ContractGrain.Reviewed.For("round-2")`:

:::code source="snippets/durable-tasks/Orleans0214.cs" id="orleans0214_fix" language="csharp":::

<a id="when-to-suppress"></a>

## 何时压缩

禁止循环的诊断,因为循环会等待每次循环中同一一次性事件,例如,在事件到达后重新检查一个条件。
