---
title: "ORLEANS0228: 分离持久化 Grain 调用"
description: 了解 ORLEANS0228，它报告对无法作为根任务运行的步骤、异步 DurableTask 方法或组合器使用 Detached 的情况，并介绍如何分离持久化 Grain 调用，或以附属方式启动工作。
ms.date: 10/19/2026
ms.topic: reference
---

<a id="orleans0228-detach-durable-grain-calls"></a>

# ORLEANS0228: 分离持久化 Grain 调用

| 属性 | 值 |
| --- | --- |
| 严重性 | 错误 |
| 类别 | 用法 |
| 代码修复 | 无 |

<a id="cause"></a>

## 原因

持久化任务代码对只能作为启动它的执行实例的一部分运行的定义调用 `Detached()` 或 `Detached(rootId)`，例如 `DurableTask.Run` 步骤、`async DurableTask` 方法或 `DurableTask.WhenAll` 等组合器：

:::code source="snippets/durable-tasks/Orleans0228.cs" id="orleans0228_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

分离任务是独立的根任务。宿主存储该任务，并在启动它的执行实例完成及该执行实例的执行树被删除之后仍独立运行该任务。因此，其定义必须能够由宿主作为根任务启动，例如持久化 Grain 调用，Grain 的宿主会将其存储为请求。

步骤、`async DurableTask` 方法和组合器是启动它们的执行实例中的代码：只有该执行实例的重放能够重新创建它们。以分离方式启动此类操作会抛出 `InvalidOperationException`，且发生在启动操作从执行实例获取操作标识符之前。

有关附属和分离启动，请参阅[启动和观察任务](../grains/durable-tasks.md#starting-and-observing-tasks)。

<a id="how-to-fix-violations"></a>

## 如何修复违规

**分离持久化 Grain 调用。** 将工作移入 Grain 的持久化方法，并分离该调用。Grain 将其作为根任务运行：

:::code source="snippets/durable-tasks/Orleans0228.cs" id="orleans0228_fix_grain" language="csharp":::

**以附属方式启动工作。** 如果执行实例可以等待该工作，则直接等待它，或调度它并观察其句柄：

:::code source="snippets/durable-tasks/Orleans0228.cs" id="orleans0228_fix_attached" language="csharp":::

<a id="when-to-suppress"></a>

## 何时抑制诊断

请保持此诊断启用：此类启动操作会在运行时抛出异常。
