---
title: "ORLEANS0228:分离耐 grain 调用"
description: "理解 ORLEANS0228, 其中报告在一步上脱钩、 一种同步 DurableTask 方法或组合器上出现断层, 无法作为根任务运行, 并通过解开耐用 grain 调用或启动附加的工作来修正它 。"
ms.date: 10/19/2026
ms.topic: reference
---

<a id="orleans0228-detach-durable-grain-calls"></a>

# ORLEANS0228:分离耐 grain 调用

| 财产 | 价值值值 |
| --- | --- |
| 严重性 | 错误错误 |
| 类别类 | 用法 |
| 代码修正 | 无无无无 |

<a id="cause"></a>

## 原因原因原因

耐久任务代码在作为开始执行的一部分而运行的定义上调用 `Detached()` 或 `Detached(rootId)` :一个 `DurableTask.Run` 步骤、一个 `async DurableTask` 方法,或一个组合器,如 `DurableTask.WhenAll`:

:::code source="snippets/durable-tasks/Orleans0228.cs" id="orleans0228_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

独立任务是一个独立的根任务。 其主机将任务存储并自行运行, 在开始执行完成后和开始执行后树树被删除后, 所以它的定义是, 主机开始作为根任务, 比如耐用 grain 调用 grain请求提供东道商店。

一步、 `async DurableTask` 方法和组合器是开始执行的代码:执行的重播可以重新生成它们,其他东西也无从创建。在开始之前先启动一个分离投出 `InvalidOperationException`,然后才采集执行的识别符号。

附加启动和分离启动,见 [开始和观察任务](../grains/durable-tasks.md#starting-and-observing-tasks)。

<a id="how-to-fix-violations"></a>

## 如何纠正违反情况

**调出一个耐久 grain 呼叫。** 将工作移入一个耐久的 grain方法,并拆开该呼叫。 grain 将它作为根任务运行:

:::code source="snippets/durable-tasks/Orleans0228.cs" id="orleans0228_fix_grain" language="csharp":::

**开始附加的工作。** 当执行过程可以等待、等待或安排并观察其操作时:

:::code source="snippets/durable-tasks/Orleans0228.cs" id="orleans0228_fix_attached" language="csharp":::

<a id="when-to-suppress"></a>

## 何时压缩

保持诊断功能: 启动时抛出 。