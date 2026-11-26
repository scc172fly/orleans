---
title: "ORLEANS0205:等待、安排或张贴耐久任务定义"
description: "理解 ORLEANS0205,其中报告一个长期的任务定义,该定义代码创建、从未运行,并通过等待、安排或张贴来加以确定。"
ms.date: 10/15/2026
ms.topic: reference
---

<a id="orleans0205-await-schedule-or-post-durable-task-definitions"></a>

# ORLEANS0205:等待、安排或张贴耐久任务定义

| 财产 | 价值值值 |
| --- | --- |
| 严重性 | 错误错误 |
| 类别类 | 用法 |
| 代码修正 | 无无无无 |

<a id="cause"></a>

## 原因原因原因

代码调用一种返回 `DurableTask` 或 `DurableTask<TResult>`的方法,或者以 `WithId`、 `Detached`或 `Shielded`配置结果,并且丢弃数值而不等待、排期或张贴:

:::code source="snippets/durable-tasks/Orleans0205.cs" id="orleans0205_violation" language="csharp":::

C# 编译者也报告 CS4014 某些发言的 `async` 方法。 ORLEANS0205 也包括分配给弃鱼的定义, `_ = SendReceipt(customer);`,配置,例如 `SendReceipt(customer).WithId("receipt");`以及当地人的定义,即没有代码等待、计划、张贴、回信或传递,例如 `var receipt = SendReceipt(customer);` 和继 `return;`。本地的,而该本地的仅另对本地的 `var named = receipt.WithId("receipt");`,也计为未使用,而代码指定的地方则没有报告。

<a id="rule-description"></a>

## 规则说明

一项持久的任务是寒冷的:调用它的方法会创建定义,而定义的配置则返回其配置。任务既不开始,也不开始。任务在等待定义时运行,时间定在 `ScheduleAsync`,或以 `Post`。废弃的定义从不运行,因此示例中的收据从未发送,也没有错误报告。

任务启动方式,见 [开始和观察任务](../grains/durable-tasks.md#starting-and-observing-tasks)。

<a id="how-to-fix-violations"></a>

## 如何纠正违反情况

**等待定义** 来运行任务并继续其结果:

:::code source="snippets/durable-tasks/Orleans0205.cs" id="orleans0205_fix_await" language="csharp":::

**排定时间,等待处理程序稍后** 与其他工作同时运行。 `ScheduleAsync` 一旦东道方接受任务处理, 即返回该任务处理:

:::code source="snippets/durable-tasks/Orleans0205.cs" id="orleans0205_fix_schedule" language="csharp":::

**通知它** ,以在不等待的情况下用下一期处决日记写开始。 `Post` 返回手柄,执行过程稍后可以等待:

:::code source="snippets/durable-tasks/Orleans0205.cs" id="orleans0205_fix_post" language="csharp":::

耐用 grain 呼叫,启动器不等待作为独立任务运行的, `.Detached().Post()`,该呼叫独立于启动器独立运行。

<a id="when-to-suppress"></a>

## 何时压缩

禁止通过分析器没有遵循的路径进行诊断, 从而将定义传递到代码中, 代码从稍后开始。
