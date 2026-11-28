---
title: "ORLEANS0214: 为每次重复等待派生信号名称"
description: 了解 ORLEANS0214，它报告在循环中等待同一个持久化信号名称的情况，并介绍如何使用 DurableSignal<T>.For 修复。
ms.date: 10/15/2026
ms.topic: reference
---

<a id="orleans0214-derive-a-signal-name-for-each-repeated-wait"></a>

# ORLEANS0214: 为每次重复等待派生信号名称

| 属性 | 值 |
| --- | --- |
| 严重性 | 信息 |
| 类别 | 用法 |
| 代码修复 | 无 |

<a id="cause"></a>

## 原因

持久化任务主体在循环中调用 `WaitAsync` 或 `TryWaitAsync`，调用对象为 `DurableSignal<T>`，且未使用 `For` 派生信号：

:::code source="snippets/durable-tasks/Orleans0214.cs" id="orleans0214_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

对于每个信号名称，任务记录发送方传递的第一个值，并将该值返回给对此名称的每次等待，包括重放期间的等待。此后，同一名称收到的值若不同，则作为冲突拒绝；若相同，则报告为重复。

在示例中，第一次审核拒绝了合同。后续每轮等待都会立即返回这次拒绝结果，因此协商在一次审核后便结束，审核人员之后的决定也会被拒绝。

有关信号，请参阅[信号](../grains/durable-tasks.md#signals)。

<a id="how-to-fix-violations"></a>

## 如何修复违规

使用 `DurableSignal<T>.For` 根据能够标识每次等待的值（例如轮次编号），为每次等待派生名称。发送方将每次决定传递给相应的派生信号，例如 `ContractGrain.Reviewed.For("round-2")`：

:::code source="snippets/durable-tasks/Orleans0214.cs" id="orleans0214_fix" language="csharp":::

<a id="when-to-suppress"></a>

## 何时抑制诊断

如果循环有意在每次迭代中等待同一个一次性事件，例如在事件到达后重新检查条件，则可抑制此诊断。
