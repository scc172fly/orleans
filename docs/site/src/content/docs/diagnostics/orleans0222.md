---
title: "ORLEANS0222:交回一项不同步工作任务的通行证代表"
description: "理解 ORLEANS0222,而据报告,Async无效的代表通过 DurableTask.Run 和 DurableTask.RunInline,并用返回任务或同步代表来调整它们。 DurableTask 方法。"
ms.date: 10/19/2026
ms.topic: reference
---

<a id="orleans0222-pass-delegates-which-return-a-task-for-asynchronous-work"></a>

# ORLEANS0222:交回一项不同步工作任务的通行证代表

| 财产 | 价值值值 |
| --- | --- |
| 严重性 | 错误错误 |
| 类别类 | 用法 |
| 代码修正 | 无无无无 |

<a id="cause"></a>

## 原因原因原因

`DurableTask.Run` 或 `DurableTask.RunInline` 接收代表,该代表返回 `void` 并约束一个 `async` 函数:一个 `async` 羊羔转换成 `Action` 或 `Action<CancellationToken>`,一种 `async void` 方法,或持有以下功能的当地人:

:::code source="snippets/durable-tasks/Orleans0222.cs" id="orleans0222_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

`async void` 代表返回到第一个未完全的 `await`。 `DurableTask.Run` 和 `DurableTask.RunInline` 代表返回时记录代表完成的工作,因此记录先于 `await`之后的工作:

- 步骤或效果完成,身体继续,而工作仍在进行。
- 重放返回记录时不运行代表, 因此工作只在第一时间进行 。
- 工作在 `await` 后丢弃的例外,是跳出任务而不是失败步骤或效果。

例如,电子邮件发送前该步骤就已完成,发送失败后该步骤的记录就会成功。

步骤和效果见 [效果和步骤](../grains/durable-tasks.md#effects-and-steps)。

<a id="how-to-fix-violations"></a>

## 如何纠正违反情况

**返回任务。** `DurableTask.Run` 与一个代表一起返回 `Task` ,在任务完成后完成该步骤,并记录任务结果:

:::code source="snippets/durable-tasks/Orleans0222.cs" id="orleans0222_fix_task" language="csharp":::

**以 `async DurableTask` 方法运行工作。** 该方法是身体等待的一个步骤:

:::code source="snippets/durable-tasks/Orleans0222.cs" id="orleans0222_fix_durable_method" language="csharp":::

`DurableTask.RunInline` 仅运行同步工作。 同步工作作为一步运行 。

<a id="when-to-suppress"></a>

## 何时压缩

仅因火灾和遗忘工作而抑制诊断,而这项任务既未记录也未等待,代表自己处理其失败。
