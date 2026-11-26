---
title: "ORLEANS0209:在排程器上保持持久任务机构"
description: "理解 ORLEANS0209, 其中报告长期任务代码, 即启动线条、计时器或线索库工作, 并用机构等待的步骤或服务来修正它 。"
ms.date: 10/15/2026
ms.topic: reference
---

<a id="orleans0209-keep-durable-task-bodies-on-their-scheduler"></a>

# ORLEANS0209:在排程器上保持持久任务机构

| 财产 | 价值值值 |
| --- | --- |
| 严重性 | 警告 |
| 类别类 | 用法 |
| 代码修正 | 无无无无 |

<a id="cause"></a>

## 原因原因原因

持久任务机构、 `DurableTask.Run` 代表或 `DurableTask.RunInline` 效应开始在另一个排程器上工作: `Task.Run`、 `TaskFactory.StartNew`、 `Parallel` 方法、 `ThreadPool.QueueUserWorkItem`、新的 `Thread`或一个新的 `System.Threading.Timer`、 `System.Timers.Timer`或 `PeriodicTimer`:

:::code source="snippets/durable-tasks/Orleans0209.cs" id="orleans0209_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

耐久任务机构在启动 grain 的调度器上运行, 它一次运行一个 grain 代码。 调度器命令机的持久操作, 所以每个运行都以相同的顺序创建它们, 它会保持身体状态的变化, 而不是与 grain的其他工作竞争。 `Task.Run` 在线性集合中执行代表; 一个正常的 `await Task.Run(...)` 捕捉激活的调度器并在那里恢复尸体。 离位代表承担风险 :

- 代表内部的代码在激活之外。 `DurableTask.RunInline` 和 `Post` 在那里投放 `InvalidOperationException` ,因为他们随着激活状态的变化而进行记录。
- Grain 说明工作变化与 grain的方法和其他处决的比赛情况,以及任何日记书中的下列内容所实现的变化。
- 线条和计时器比开始它们的时间运行时间长, 重播又重新开始它们。

ORLEANS0014 报告 `ConfigureAwait(false)`,其中也移动启动的续续。

<a id="how-to-fix-violations"></a>

## 如何纠正违反情况

**记录工作效果。 A** A `DurableTask.RunInline` 效果在激活的调度器上同步运行,结果被记录, 因此重放返回它而不再次计算 :

:::code source="snippets/durable-tasks/Orleans0209.cs" id="orleans0209_fix_inline" language="csharp":::

**等待一个运行工作的服务。 对于CPU密集型的工作,** 从一个阶梯上调用服务或无国籍工人 grain 。该服务使用自己的线条,该阶梯的继续返回激活:

:::code source="snippets/durable-tasks/Orleans0209.cs" id="orleans0209_fix_service" language="csharp":::

**替换时间轴以耐久延迟代替。** `DurableTask.Delay` 和 `DurableTask.DelayUntil` 在到期时恢复任务,包括在停止运行之后。定期工作是按命名的延误循环进行,或作为每次迭代开始的一项独立任务进行。

<a id="when-to-suppress"></a>

## 何时压缩

禁止对工作进行诊断,因为工作没有触及 grain 状态,没有计划持久操作,而且身体在线性集合上运行时需要等待完成,例如纯粹计算。
