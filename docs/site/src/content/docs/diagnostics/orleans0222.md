---
title: "ORLEANS0222: 为异步工作传入返回任务的委托"
description: 了解 ORLEANS0222，它报告传给 DurableTask.Run 和 DurableTask.RunInline 的 async void 委托，并介绍如何使用返回任务的委托或异步 DurableTask 方法修复。
ms.date: 10/19/2026
ms.topic: reference
---

<a id="orleans0222-pass-delegates-which-return-a-task-for-asynchronous-work"></a>

# ORLEANS0222: 为异步工作传入返回任务的委托

| 属性 | 值 |
| --- | --- |
| 严重性 | 错误 |
| 类别 | 用法 |
| 代码修复 | 无 |

<a id="cause"></a>

## 原因

`DurableTask.Run` 或 `DurableTask.RunInline` 接收返回 `void` 的委托，该委托绑定到 `async` 函数：例如将 `async` Lambda 表达式转换为 `Action` 或 `Action<CancellationToken>`，使用 `async void` 方法，或使用持有这种委托的局部变量：

:::code source="snippets/durable-tasks/Orleans0222.cs" id="orleans0222_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

`async void` 委托在遇到第一个尚未完成的 `await` 时就返回调用方。`DurableTask.Run` 和 `DurableTask.RunInline` 在委托返回时记录其完成，因此该记录早于 `await` 之后的工作：

- 工作仍在运行时，步骤或效果就已经完成，主体也继续执行。
- 重放直接返回记录而不运行委托，因此工作只在首次运行中执行。
- 工作在 `await` 之后抛出的异常会传播到任务之外，而不会使步骤或效果失败。

在示例中，步骤在电子邮件发送之前就已完成，即使发送失败，该步骤的记录仍显示成功。

有关步骤和效果，请参阅[效果与步骤](../grains/durable-tasks.md#effects-and-steps)。

<a id="how-to-fix-violations"></a>

## 如何修复违规

**返回任务。** 使用 `DurableTask.Run` 并传入返回 `Task` 的委托，步骤会在任务完成时完成，并记录任务的结果：

:::code source="snippets/durable-tasks/Orleans0222.cs" id="orleans0222_fix_task" language="csharp":::

**在 `async DurableTask` 方法中运行工作。** 该方法是主体等待的一个步骤：

:::code source="snippets/durable-tasks/Orleans0222.cs" id="orleans0222_fix_durable_method" language="csharp":::

`DurableTask.RunInline` 仅运行同步工作。异步工作应作为步骤运行。

<a id="when-to-suppress"></a>

## 何时抑制诊断

仅当工作是任务既不记录也不等待的即发即弃工作，且其失败由委托自行处理时，才可抑制此诊断。
