---
title: "ORLEANS0208: 为每个持久化操作调用点赋予独有名称"
description: 了解 ORLEANS0208：同一主体内的两个持久化操作调用点使用同名。通过分别命名各调用点来修复。
ms.date: 10/15/2026
ms.topic: reference
---

<a id="orleans0208-give-each-durable-operation-call-site-its-own-name"></a>

# ORLEANS0208: 为每个持久化操作调用点赋予独有名称

| 属性 | 值 |
| --- | --- |
| 严重性 | 警告 |
| 类别 | 用法 |
| 代码修复 | 无 |

<a id="cause"></a>

## 原因

同一持久化任务主体内的两个 `WithId` 调用点，或两个 `DurableTask.RunInline` 调用点，传入相同的常量名称，且一次运行可能到达两者。互斥分支（如 `if` 语句的两个分支）中的调用点，以及循环重复执行的单个调用点，不会被报告。在持久化任务主体之外（如客户端和普通 Grain 方法），`WithId` 为根任务命名，此规则不检查它们：

:::code source="snippets/durable-tasks/Orleans0208.cs" id="orleans0208_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

一次运行中重复使用的名称会按出现顺序标识后续操作：第一次出现为 `email`，第二次为 `email$2`。因此每个标识符取决于此前有多少调用点使用该名称。

当部署在仍有挂起执行时移除了欢迎邮件操作，已发送两封邮件的执行在重放时会将提示邮件命名为 `email`。此标识符保存的是欢迎邮件记录：

- 对于本地步骤，重放返回欢迎邮件的已记录完成结果，而提示邮件自身的记录 `email$2` 永远不会被访问。
- 对于调用其他 Grain 的操作，重放会在该标识符下找到另一个请求，导致分歧并使执行停滞。

不同的名称使各调用点在其他调用点被添加、移除或重排时仍保持自身标识符。有关标识符构造方式，请参阅[任务标识和引用](../grains/durable-tasks-identity.md)。

有关标识符及版本控制，请参阅[重放安全规则](../grains/durable-tasks-replay-rules.md)。

<a id="how-to-fix-violations"></a>

## 如何修复违规

**为每个调用点使用独有名称：**

:::code source="snippets/durable-tasks/Orleans0208.cs" id="orleans0208_fix" language="csharp":::

**重复执行的调用点只命名一次。** 循环中的调用点保持同一个名称，运行时按出现顺序编号。如需改为按输入标识每次出现，则从稳定值派生名称，例如 `$"lesson-{index}"`：

:::code source="snippets/durable-tasks/Orleans0208.cs" id="orleans0208_fix_loop" language="csharp":::

<a id="when-to-suppress"></a>

## 何时抑制

当分析器无法跟踪的条件保证两处调用点不可能在同一次执行中都执行到时，可以抑制诊断。
