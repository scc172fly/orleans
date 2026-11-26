---
title: "ORLEANS0226:继续按时间表安排设立持久任务机构"
description: "理解 ORLEANS0226,报告 ConfigureAwait 调用将一个持久任务机构继续其 grain排期表上调出的工作单位,并在不填 ConfigureAwait的情况下进行等待来修正。"
ms.date: 10/19/2026
ms.topic: reference
---

<a id="orleans0226-continue-durable-task-bodies-on-their-scheduler"></a>

# ORLEANS0226:继续按时间表安排设立持久任务机构

| 财产 | 价值值值 |
| --- | --- |
| 严重性 | 错误错误 |
| 类别类 | 用法 |
| 代码修正 | 无无无无 |

<a id="cause"></a>

## 原因原因原因

包括一个步骤在内的持久任务机构等待 `ConfigureAwait(false)`或 `ConfigureAwaitOptions` ,而其中省略了 `ContinueOnCapturedContext`:

:::code source="snippets/durable-tasks/Orleans0226.cs" id="orleans0226_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

耐久任务机构在 grain 激活的调度器上运行, 它一次运行 grain代码的一部分。 排程者命令机的耐久操作及其状态变化, 运行时间将身体结果与身体尾部进行的国家变化连接起来 。

等待时间持续,没有被捕获的上下文,尸体又重新留在线条池中:

- 诸如 `DurableTask.RunInline`等持久操作,任务开始和 `DurableTask.GetVersion`,在那里投掷 `InvalidOperationException` ,因为执行命令并记录在时间表上。
- 国家改变与 grain其他回合的种族,包括方式和其他处决。
- 尾巴的状态变化和身体结果分别承诺。在示例中, `_lastRate` 发生后和结果产生前的崩溃再次运行尾巴, 以获取一个利率并再次指定 `_lastRate` 。

ORLEANS0014 报告 `ConfigureAwait(false)` ,以一般 grain 代码填报。

身体的尾巴见 [重放代码、记录操作和尾尾](../grains/durable-tasks-replay-rules.md#replayed-code-recorded-operations-and-the-tail)。

<a id="how-to-fix-violations"></a>

## 如何纠正违反情况

**等不等 `ConfigureAwait`.** 继续返回激活的调度器 `ConfigureAwait(true)` 和 `ConfigureAwaitOptions.ContinueOnCapturedContext` 继续:

:::code source="snippets/durable-tasks/Orleans0226.cs" id="orleans0226_fix" language="csharp":::

身体等待保存自己的 `ConfigureAwait(false)`的图书馆方法:规则检查身体等待的情况,而身体等待图书馆的方法恢复在排程器上。

<a id="when-to-suppress"></a>

## 何时压缩

保持诊断功能: 耐用操作会让调度器停用, 并且状态会随着 grain的其他工作而发生变化。
