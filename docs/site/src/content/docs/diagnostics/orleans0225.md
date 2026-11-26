---
title: "ORLEANS0225:等待持久任务,而不是在长期任务机构投票选举这些任务"
description: "了解 ORLEANS0225,其中报告在长期任务机构对既定任务的民意测验,并进行投票,通过等待或等待持久超时来解决这些问题。"
ms.date: 10/19/2026
ms.topic: reference
---

<a id="orleans0225-await-durable-tasks-instead-of-polling-them-in-durable-task-bodies"></a>

# ORLEANS0225:等待持久任务,而不是在长期任务机构投票选举这些任务

| 财产 | 价值值值 |
| --- | --- |
| 严重性 | 错误错误 |
| 类别类 | 用法 |
| 代码修正 | 无无无无 |

<a id="cause"></a>

## 原因原因原因

包括一步的持久任务机构,对预定任务进行 `ScheduledTask.IsCompletedAsync`、 `ScheduledTask.GetStatusAsync`或 `ScheduledTask.GetResponseAsync` 与 `PollingOptions`投票:

:::code source="snippets/durable-tasks/Orleans0225.cs" id="orleans0225_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

民意测验报告任务运行时的状况,而执行记录对此没有记录。 在另一个时间重播尸体的再运行,当民意测验观察另一状态时。 一个围绕状况(比如示例中的循环)的机构,然后安排执行记录运行以外的操作,重放则不同。

运行时丢弃 `NotSupportedException` 在一个持久任务内进行民意测验 所以尸体在第一次民调时失败了

记录结果,每次重播都看到同样的记录。 `ScheduledTask.GetResponseAsync` 无 `PollingOptions` 等待终端响应,如等待。 代码在持久的任务机构之外, 如客户、 民意测验等。

控件和超时,请见 [组合器和控点](../grains/durable-tasks.md#combinators-and-handles)。

<a id="how-to-fix-violations"></a>

## 如何纠正违反情况

**等待任务。** 任务完成后,机构恢复工作,重放返回记录的结果。一个首先做其他工作的机构先安排任务,然后等待处理:

:::code source="snippets/durable-tasks/Orleans0225.cs" id="orleans0225_fix_await" language="csharp":::

**等待耐久的超时** `WaitAsync` 拖拉时间将比任务长时推延。 当延迟完成第一个时, 任务会收到取消请求, 结束的任务也会被取消 `TimeoutException`手柄 `WaitAsync` 与暂停的时间一样,等待时间相同; `ScheduledTask.WhenAny` 预定时间 `DurableTask.Delay` 返回先填全的 :

:::code source="snippets/durable-tasks/Orleans0225.cs" id="orleans0225_fix_timeout" language="csharp":::

<a id="when-to-suppress"></a>

## 何时压缩

保持诊断功能:在持久任务机构进行的民意测验会扔出 `NotSupportedException`。 民意调查属于长期任务机构以外的代码,如客户或普通的 grain 方法。
