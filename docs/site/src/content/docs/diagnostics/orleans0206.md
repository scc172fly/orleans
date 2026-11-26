---
title: "ORLEANS0206:运行每个耐久任务定义一次"
description: "理解 ORLEANS0206, 它会报告一个自动 DurableTask 定义, 代码不止一次运行, 然后通过为每次运行创建定义来修正它 。"
ms.date: 10/15/2026
ms.topic: reference
---

<a id="orleans0206-run-each-durable-task-definition-once"></a>

# ORLEANS0206:运行每个耐久任务定义一次

| 财产 | 价值值值 |
| --- | --- |
| 严重性 | 警告 |
| 类别类 | 用法 |
| 代码修正 | 无无无无 |

<a id="cause"></a>

## 原因原因原因

本地变量持有一个定义,该汇编中 `async DurableTask` 方法返回了该定义,代码第二次运行:等待、计划或将变量张贴两次,或在不创建定义的循环圈中:

:::code source="snippets/durable-tasks/Orleans0206.cs" id="orleans0206_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

A 音 A `async DurableTask` 方法返回单用定义:它持有该方法的状态机器,该机器运行一次。循环的第一迭代运行着定义。第二个迭代是抛出 `InvalidOperationException` 与消息 `A deferred durable task definition can execute only once.`,它未能完成任务后,它第一次提醒。

定义见 [开始和观察任务](../grains/durable-tasks.md#starting-and-observing-tasks)。

<a id="how-to-fix-violations"></a>

## 如何纠正违反情况

**调用每次运行的方法。** 每个调用为一次运行设定定义 :

:::code source="snippets/durable-tasks/Orleans0206.cs" id="orleans0206_fix" language="csharp":::

**保留工厂,而不是定义。** 本地函数或调用该方法的代表在每次运行时都创建定义:

:::code source="snippets/durable-tasks/Orleans0206.cs" id="orleans0206_fix_factory" language="csharp":::

<a id="when-to-suppress"></a>

## 何时压缩

当到达第二波的路径相互排斥时, 停止诊断, 所以每次执行都会运行一次定义 。
