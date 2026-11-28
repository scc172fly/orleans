---
title: "ORLEANS0213: 持久化任务主体中的异步代码应返回任务"
description: 了解 ORLEANS0213：持久化任务主体中的 async void 方法、局部函数和 Lambda 表达式。通过返回并等待任务来修复。
ms.date: 10/15/2026
ms.topic: reference
---

<a id="orleans0213-return-a-task-from-asynchronous-code-in-durable-task-bodies"></a>

# ORLEANS0213: 持久化任务主体中的异步代码应返回任务

| 属性 | 值 |
| --- | --- |
| 严重性 | 警告 |
| 类别 | 用法 |
| 代码修复 | 无 |

<a id="cause"></a>

## 原因

持久化任务主体声明 `async void` 局部函数，或声明 `async` Lambda 表达式，该表达式转换为返回 `void` 的委托，例如 `Action`。[ORLEANS0222](orleans0222.md)报告由 `DurableTask.Run` 或 `DurableTask.RunInline` 接收的此类 Lambda 表达式：

:::code source="snippets/durable-tasks/Orleans0213.cs" id="orleans0213_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

持久化任务主体观察它所等待的操作，包括结果、失败及取消。异步 `void` 代码不能被等待。在示例中：

- 在邮件发送之前，主体已经返回，且任务结果已提交。
- 发送失败逸出主体，因此任务成功完成，异常却在任务之外出现。
- 代码在第一次 `await` 之后执行的持久化操作，会在主体完成之后、任务执行之外运行。

有关规则与重放模型，请参阅[重放安全规则](../grains/durable-tasks-replay-rules.md)。

<a id="how-to-fix-violations"></a>

## 如何修复违规

**返回 `DurableTask` 并将其作为步骤等待。** 主体观察步骤结果，重放返回其记录：

:::code source="snippets/durable-tasks/Orleans0213.cs" id="orleans0213_fix_step" language="csharp":::

**从回调返回 `Task`。** 对异步回调使用 `Func<Task>` 而非 `Action`，并在步骤中等待回调：

:::code source="snippets/durable-tasks/Orleans0213.cs" id="orleans0213_fix_func" language="csharp":::

<a id="when-to-suppress"></a>

## 何时抑制

不要抑制此诊断。签名要求 `void` 的事件处理程序应位于持久化任务主体之外，在那里可以通过其 Grain 启动持久化任务。
