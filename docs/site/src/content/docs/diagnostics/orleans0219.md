---
title: "ORLEANS0219: 等待、组合或返回已调度子任务的句柄"
description: 了解 ORLEANS0219，它报告持久化任务主体从未观察已调度子任务句柄的情况，并介绍通过等待句柄、组合句柄或分离任务进行修复的方法。
ms.date: 10/19/2026
ms.topic: reference
---

<a id="orleans0219-await-combine-or-return-the-handles-of-scheduled-children"></a>

# ORLEANS0219: 等待、组合或返回已调度子任务的句柄

| 属性 | 值 |
| --- | --- |
| 严重性 | 信息 |
| 类别 | 用法 |
| 代码修复 | 无 |

<a id="cause"></a>

## 原因

持久化任务主体使用 `ScheduleAsync` 调度附属子任务，却丢弃返回的句柄，或将其保存在仅用于读取属性（例如 `Id`）的局部变量中：

:::code source="snippets/durable-tasks/Orleans0219.cs" id="orleans0219_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

在持久化任务主体中，`ScheduleAsync` 启动附属子任务，并在子任务的宿主接受并存储它之后返回其句柄。执行实例拥有该子任务：执行实例的取消会传递到子任务；执行实例进入终止状态后，删除其执行树也会取消仍在运行的子任务。只有观察句柄的代码才能获知子任务的结果和失败。

在示例中，主体完成时通知可能仍在运行。如果通知发送失败，订单仍处于已发货状态，而工作流中没有失败记录；如果执行树被删除时通知仍在运行，该通知任务会被取消。

有关附属和分离启动，请参阅[启动和观察任务](../grains/durable-tasks.md#starting-and-observing-tasks)。

<a id="how-to-fix-violations"></a>

## 如何修复违规

**等待句柄。** 子任务在主体继续执行时运行，主体在完成前观察其结果：

:::code source="snippets/durable-tasks/Orleans0219.cs" id="orleans0219_fix_await" language="csharp":::

**将句柄传给组合器。** `ScheduledTask.WhenAll` 等待所有句柄，并按输入顺序抛出第一个失败异常：

:::code source="snippets/durable-tasks/Orleans0219.cs" id="orleans0219_fix_combinator" language="csharp":::

**返回句柄。** 返回句柄的主体将观察结果的责任交给调用方。

**分离生命周期超过当前执行实例的工作。** `Detached()` 启动独立的根任务，它在当前执行实例完成及其执行树被删除后仍会继续运行：

:::code source="snippets/durable-tasks/Orleans0219.cs" id="orleans0219_fix_detached" language="csharp":::

<a id="when-to-suppress"></a>

## 何时抑制诊断

如果工作流有意忽略子任务的结果，且允许它在执行树被删除时停止（例如尽力而为的缓存预热），则可抑制此诊断。
