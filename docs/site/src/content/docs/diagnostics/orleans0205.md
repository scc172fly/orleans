---
title: "ORLEANS0205: 等待、调度或投递持久化任务定义"
description: 了解 ORLEANS0205：代码创建持久化任务定义却从未运行它。通过等待、调度或投递来修复。
ms.date: 10/15/2026
ms.topic: reference
---

<a id="orleans0205-await-schedule-or-post-durable-task-definitions"></a>

# ORLEANS0205: 等待、调度或投递持久化任务定义

| 属性 | 值 |
| --- | --- |
| 严重性 | 错误 |
| 类别 | 用法 |
| 代码修复 | 无 |

<a id="cause"></a>

## 原因

代码调用返回 `DurableTask` 或 `DurableTask<TResult>` 的方法，或者使用 `WithId`、`Detached` 或 `Shielded` 配置结果，却在未等待、调度或投递的情况下丢弃该值：

:::code source="snippets/durable-tasks/Orleans0205.cs" id="orleans0205_violation" language="csharp":::

C# 编译器也会对 `async` 方法中的部分这类语句报告 CS4014。ORLEANS0205 还覆盖赋值给弃元的定义（`_ = SendReceipt(customer);`）、配置（如 `SendReceipt(customer).WithId("receipt");`），以及持有定义却没有代码等待、调度、投递、返回或传递该定义的局部变量（如 `var receipt = SendReceipt(customer);` 后接 `return;`）。仅被其他局部变量重命名或配置的局部变量（如 `var named = receipt.WithId("receipt");`）也算未使用；代码再次对其赋值的局部变量则不会被报告。

<a id="rule-description"></a>

## 规则说明

持久化任务是冷任务：调用其方法只创建定义，配置定义只返回该定义的一种配置。两者均不启动任务。只有等待定义、使用 `ScheduleAsync` 调度或使用 `Post` 启动时，任务才运行。丢弃的定义永远不会运行，因此示例中的收据永远不会发送，也不会有错误报告此事。

有关启动任务的方式，请参阅[启动和观察任务](../grains/durable-tasks.md#starting-and-observing-tasks)。

<a id="how-to-fix-violations"></a>

## 如何修复违规

**等待定义**，运行任务并使用其结果继续执行：

:::code source="snippets/durable-tasks/Orleans0205.cs" id="orleans0205_fix_await" language="csharp":::

**调度任务，之后再等待句柄**，使其与其他工作并发运行。`ScheduleAsync` 在任务宿主接受任务之后返回句柄：

:::code source="snippets/durable-tasks/Orleans0205.cs" id="orleans0205_fix_schedule" language="csharp":::

**投递任务**，无需等待即可暂存启动操作，使其随执行的下一次日志写入提交。`Post` 返回句柄，执行稍后可以等待它：

:::code source="snippets/durable-tasks/Orleans0205.cs" id="orleans0205_fix_post" language="csharp":::

对于启动方不等待的持久化 Grain 调用，将其作为分离任务运行（`.Detached().Post()`），使其独立于启动方执行。

<a id="when-to-suppress"></a>

## 何时抑制

当定义被传给稍后启动它的代码，且分析器无法跟踪该路径时，可以抑制诊断。
