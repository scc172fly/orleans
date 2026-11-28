---
title: "ORLEANS0206: 每个持久化任务定义只运行一次"
description: 了解 ORLEANS0206：代码多次运行 async DurableTask 定义。通过为每次运行创建定义来修复。
ms.date: 10/15/2026
ms.topic: reference
---

<a id="orleans0206-run-each-durable-task-definition-once"></a>

# ORLEANS0206: 每个持久化任务定义只运行一次

| 属性 | 值 |
| --- | --- |
| 严重性 | 警告 |
| 类别 | 用法 |
| 代码修复 | 无 |

<a id="cause"></a>

## 原因

局部变量持有同一编译单元中的 `async DurableTask` 方法返回的定义，而代码第二次运行它：对变量等待、调度或投递两次，或在不创建定义的循环内执行这些操作：

:::code source="snippets/durable-tasks/Orleans0206.cs" id="orleans0206_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

`async DurableTask` 方法返回一次性定义：它持有只能运行一次的方法状态机。循环的第一次迭代运行该定义，第二次迭代运行时抛出 `InvalidOperationException`，消息为 `A deferred durable task definition can execute only once.`，使任务在首次提醒之后失败。

有关定义，请参阅[启动和观察任务](../grains/durable-tasks.md#starting-and-observing-tasks)。

<a id="how-to-fix-violations"></a>

## 如何修复违规

**每次运行都调用方法。** 每次调用创建供一次运行使用的定义：

:::code source="snippets/durable-tasks/Orleans0206.cs" id="orleans0206_fix" language="csharp":::

**保留工厂而不是定义。** 调用该方法的局部函数或委托，会在主体每次执行它时创建新的定义：

:::code source="snippets/durable-tasks/Orleans0206.cs" id="orleans0206_fix_factory" language="csharp":::

<a id="when-to-suppress"></a>

## 何时抑制

当到达第二次运行的路径互斥、每次执行实际上只运行该定义一次时，可以抑制诊断。
