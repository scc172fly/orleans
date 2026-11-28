---
title: "ORLEANS0220: 为位置可能变化的持久化操作命名"
description: 了解 ORLEANS0220，它报告位于循环或条件分支中，或位于其他未命名操作之后的未命名持久化操作，并介绍使用 WithId 或 DurableTask.RunInline 的名称参数修复的方法。
ms.date: 10/19/2026
ms.topic: reference
---

<a id="orleans0220-name-durable-operations-whose-position-can-change"></a>

# ORLEANS0220: 为位置可能变化的持久化操作命名

| 属性 | 值 |
| --- | --- |
| 严重性 | 信息 |
| 类别 | 用法 |
| 代码修复 | 可用 |

<a id="cause"></a>

## 原因

持久化任务主体在循环、条件分支中，或在另一个同类未命名操作之后运行未命名的持久化操作。未命名操作包括：主体未使用 `WithId` 就等待、调度或投递的定义，没有名称的 `DurableTask.RunInline` 效果，以及未使用 `WithId` 就等待的组合器：

:::code source="snippets/durable-tasks/Orleans0220.cs" id="orleans0220_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

未命名操作采用执行实例生成的下一个标识符，该标识符基于此前已运行的同类未命名操作数量。子任务、效果和分离启动共用一个计数序列，例如 `$child-1`、`$child-2` 等；被等待的组合器则从独立计数序列获取操作标识符，例如 `$when-all-1`。主体直接等待的信号等待作为执行实例的一部分运行，不获取标识符。

在示例中，积分效果在重复订单的执行实例中标识为 `$child-1`，在首次订单的执行实例中标识为 `$child-2`。同一执行实例在相同代码上的重放会复现该计数。若新部署在其之前增加、删除或移动未命名操作，计数就会偏移，使重放使用已为另一操作记录的标识符调度操作。重放因而发生偏离：宿主使用 `DurableTaskAttemptCancellationReason.Diverged` 取消此次执行尝试，并使任务保持待处理状态。

名称通过调用位置标识操作。同一次运行中执行多次的调用位置（例如循环中的位置）保留同一名称，后续各次执行按顺序取得 `name$2`、`name$3` 等标识符。

有关标识符，请参阅[跨重放和部署的稳定性](../grains/durable-tasks-identity.md#stability-across-replays-and-deployments)；有关未命名效果，请参阅[效果与步骤](../grains/durable-tasks.md#effects-and-steps)。

<a id="how-to-fix-violations"></a>

## 如何修复违规

**为每个操作命名。** 使用 `WithId` 为定义和组合器命名，并将名称作为 `DurableTask.RunInline` 的第一个参数传入：

:::code source="snippets/durable-tasks/Orleans0220.cs" id="orleans0220_fix" language="csharp":::

代码修复为定义添加 `WithId`，名称使用定义该操作的方法名的短横线命名形式，去掉 `Async` 后缀，例如使用 `send-welcome` 对应 `SendWelcome`。如果主体已经使用该名称，则添加数字，例如 `send-welcome-2`。请手动为 `DurableTask.RunInline` 效果命名。

名称会改变操作的标识符。应在使用这些操作的执行实例进入待处理状态之前完成命名，或使用 `DurableTask.GetVersion` 为此变更设置版本分支。

<a id="when-to-suppress"></a>

## 何时抑制诊断

如果主体的执行实例都会在启动它们的同一版本代码上完成（例如生命周期较短的步骤），并且循环和条件仅依赖其参数及已记录结果，则可抑制此诊断。
