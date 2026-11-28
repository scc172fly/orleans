---
title: "ORLEANS0210: 使用持久化组合器运行并发的持久化工作"
description: 了解 ORLEANS0210：对持久化工作使用 Task.WhenAll、Task.WhenAny、Task.WaitAll 或 Task.WaitAny。使用 DurableTask.WhenAll、WhenAny、Race 或 ScheduleAll 修复。
ms.date: 10/15/2026
ms.topic: reference
---

<a id="orleans0210-run-concurrent-durable-work-through-durable-combinators"></a>

# ORLEANS0210: 使用持久化组合器运行并发的持久化工作

| 属性 | 值 |
| --- | --- |
| 严重性 | 警告 |
| 类别 | 用法 |
| 代码修复 | 无 |

<a id="cause"></a>

## 原因

持久化任务主体将持久化工作（例如等待持久化任务的 Lambda 表达式）传给 `Task.WhenAll`、`Task.WhenAny`、`Task.WaitAll` 或 `Task.WaitAny`：

:::code source="snippets/durable-tasks/Orleans0210.cs" id="orleans0210_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

枚举此示例中的 `Select` 时，按输入顺序调用各 Lambda 表达式，其等待捕获激活实例的调度器。每个子任务通过单独的日志写入提交其调用。持久化扇出组合器为输入赋予共享的持久化标识，并以批次方式提交其调用。

`Task` 组合器还会让未获胜的输入继续运行：`Task.WhenAny` 返回时其他输入仍在继续执行，没有任何操作取消或等待它们。`Task.WaitAll` 和 `Task.WaitAny` 会阻塞激活实例的调度器。

持久化组合器在组合器名称下按索引标识每个输入，例如 `reserve/0` 和 `reserve/1`；将输入作为一个批次调度，由一次日志写入提交，并记录哪个输入最先完成，使每次重放观察到相同结果。

有关组合器，请参阅[组合器和句柄](../grains/durable-tasks.md#combinators-and-handles)。

<a id="how-to-fix-violations"></a>

## 如何修复违规

**使用 `DurableTask.WhenAll`** 按输入顺序获取结果：

:::code source="snippets/durable-tasks/Orleans0210.cs" id="orleans0210_fix_when_all" language="csharp":::

**使用 `DurableTask.Race`** 获取首个结果。`Race` 请求取消其他输入并等待它们全部达到终态，因此没有输入比它运行得更久。`DurableTask.WhenAny` 返回首个结果并让其他输入继续运行：

:::code source="snippets/durable-tasks/Orleans0210.cs" id="orleans0210_fix_race" language="csharp":::

**使用 `DurableTask.ScheduleAll`** 启动所有输入并持有其句柄继续执行；使用 `DurableTask.WhenAllCompleted` 等待所有结果而不抛出异常。若需为持久化工作设置超时限制而不是与 `Task.Delay` 竞速，请使用任务的 `WaitAsync` 或 `WaitUntilAsync`。

<a id="when-to-suppress"></a>

## 何时抑制

当组合的任务不执行任何持久化操作，而分析器无法确定这一点时，可以抑制诊断，例如这些任务调用其他程序集中的辅助方法。
