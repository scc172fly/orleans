---
title: "ORLEANS0209: 使持久化任务主体始终在其调度器上执行"
description: 了解 ORLEANS0209：持久化任务代码启动线程、定时器或线程池工作。通过主体等待的步骤或服务修复。
ms.date: 10/15/2026
ms.topic: reference
---

<a id="orleans0209-keep-durable-task-bodies-on-their-scheduler"></a>

# ORLEANS0209: 使持久化任务主体始终在其调度器上执行

| 属性 | 值 |
| --- | --- |
| 严重性 | 警告 |
| 类别 | 用法 |
| 代码修复 | 无 |

<a id="cause"></a>

## 原因

持久化任务主体、`DurableTask.Run` 委托或 `DurableTask.RunInline` 效果在另一调度器上启动工作：`Task.Run`、`TaskFactory.StartNew`、`Parallel` 方法、`ThreadPool.QueueUserWorkItem`、新的 `Thread`，或者新的 `System.Threading.Timer`、`System.Timers.Timer` 或 `PeriodicTimer`：

:::code source="snippets/durable-tasks/Orleans0209.cs" id="orleans0209_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

持久化任务主体在其 Grain 激活实例的调度器上运行，该调度器每次运行一段 Grain 代码。调度器对主体的持久化操作排序，使每次运行按相同顺序创建操作，并防止主体状态变更与 Grain 的其他工作发生竞态。`Task.Run` 在线程池执行委托；普通的 `await Task.Run(...)` 捕获激活实例调度器，并在其上恢复主体执行。在调度器之外运行的委托存在以下风险：

- 委托内的代码在激活实例之外运行。`DurableTask.RunInline` 和 `Post` 在此处抛出 `InvalidOperationException`，因为它们将记录与激活实例的状态变更一同暂存。
- 工作修改的 Grain 状态会与 Grain 方法及其他执行发生竞态；其变更与随后发生的任何日志写入一同提交。
- 线程和定时器的生命周期超过启动它们的运行，重放会再次启动它们。

ORLEANS0014 报告 `ConfigureAwait(false)`，它同样会使延续操作离开激活实例。

<a id="how-to-fix-violations"></a>

## 如何修复违规

**在效果中记录工作。** `DurableTask.RunInline` 效果在激活实例调度器上运行同步工作，且结果被记录，因此重放直接返回结果，无需再次计算：

:::code source="snippets/durable-tasks/Orleans0209.cs" id="orleans0209_fix_inline" language="csharp":::

**等待执行工作的服务。** 对于 CPU 密集型工作，从步骤中调用服务或无状态工作者 Grain。服务使用自己的线程，步骤的延续操作返回激活实例：

:::code source="snippets/durable-tasks/Orleans0209.cs" id="orleans0209_fix_service" language="csharp":::

**以持久化延迟替换定时器。** `DurableTask.Delay` 和 `DurableTask.DelayUntil` 在到期时恢复任务，即使此前发生了停用也不例外。周期性工作可以运行由命名延迟构成的循环，或者由每次迭代启动分离任务。

<a id="when-to-suppress"></a>

## 何时抑制

如果工作不访问任何 Grain 状态、不调度持久化操作，并且主体等待其完成（例如纯计算），同时确实需要在线程池上运行，可以抑制诊断。
