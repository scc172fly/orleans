---
title: "ORLEANS0221: 使用 DurableTask.RunInline 运行同步工作"
description: 了解 ORLEANS0221，它报告仅运行同步工作的 DurableTask.Run 步骤，并介绍如何使用 DurableTask.RunInline 效果修复，以及在执行实例尚未完成时使用 DurableTask.GetVersion 控制版本分支。
ms.date: 10/19/2026
ms.topic: reference
---

<a id="orleans0221-run-synchronous-work-with-durabletaskruninline"></a>

# ORLEANS0221: 使用 DurableTask.RunInline 运行同步工作

| 属性 | 值 |
| --- | --- |
| 严重性 | 信息 |
| 类别 | 用法 |
| 代码修复 | 无 |

<a id="cause"></a>

## 原因

持久化任务主体在定义 `DurableTask.Run` 步骤的位置直接等待该步骤（可以命名或屏蔽任务取消），而步骤的 Lambda 表达式不等待任何操作、不返回任务，也不读取步骤接收到的 `CancellationToken`：

:::code source="snippets/durable-tasks/Orleans0221.cs" id="orleans0221_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

主体等待步骤时，`DurableTask.Run` 将其委托作为子执行实例运行，该实例拥有自己的 `DurableTask.Current` 和取消作用域，并可能在后续执行轮次中运行。步骤在自身的日志写入中提交结果。

`DurableTask.RunInline` 在当前工作项中运行同步工作，并在执行实例下次写入日志时，将其结果与效果所做的状态更改一起记录。连续的效果在同一次日志写入中提交。在修复示例中，状态变更及其记录随执行实例的下次日志写入提交，而不再单独写入。

同一调用位置的步骤与效果使用相同标识符，而执行实例会记录实际运行的操作类型。如果重放发现主体现在运行效果的位置原本记录的是步骤，就会发生偏离。因此，当部署仍有待处理的执行实例时，应使用 `DurableTask.GetVersion` 为变更设置版本分支。

有关效果和步骤，请参阅[效果与步骤](../grains/durable-tasks.md#effects-and-steps)。

<a id="how-to-fix-violations"></a>

## 如何修复违规

**将工作作为效果运行。** 将委托及步骤名称传给 `DurableTask.RunInline`：

:::code source="snippets/durable-tasks/Orleans0221.cs" id="orleans0221_fix" language="csharp":::

**根据版本为变更设置分支。** 如果记录过该步骤的执行实例仍处于待处理状态，则主体为新执行实例运行效果，并为已记录该步骤的执行实例保留步骤：

:::code source="snippets/durable-tasks/Orleans0221.cs" id="orleans0221_fix_versioned" language="csharp":::

<a id="when-to-suppress"></a>

## 何时抑制诊断

如果步骤确实需要独立的子执行实例（例如拥有自己的取消作用域），或当前分支是供已记录该步骤的执行实例重放的分支，则可抑制此诊断。
