---
title: "ORLEANS0210:通过耐用组合运行同时进行的持久工作"
description: "理解 ORLEANS0210,报告 Task.WhenAll, Task.WhenAny, Task.WaitAll,或 Task.WaitAny 并用一种方法解决 DurableTask.WhenAll, WhenAny,种族或 ScheduleAll."
ms.date: 10/15/2026
ms.topic: reference
---

<a id="orleans0210-run-concurrent-durable-work-through-durable-combinators"></a>

# ORLEANS0210:通过耐用组合运行同时进行的持久工作

| 财产 | 价值值值 |
| --- | --- |
| 严重性 | 警告 |
| 类别类 | 用法 |
| 代码修正 | 无无无无 |

<a id="cause"></a>

## 原因原因原因

持久任务机构通过诸如等待长期任务的羊排等持久工作,直至 `Task.WhenAll`、 `Task.WhenAny`、 `Task.WaitAll`或 `Task.WaitAny`:

:::code source="snippets/durable-tasks/Orleans0210.cs" id="orleans0210_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

在此示例中列出 `Select` , 引用每个羊羔输入顺序, 并等待捕捉激活的调度器。 每个儿童都以单独的日记来承诺使用该选项。 一个耐用扇风外组合器给输入提供共同的持久身份, 并且分批进行其引用 。

`Task` 组合器还留下丢失输入的运行时间: `Task.WhenAny` 返回而其他输入继续,没有东西取消或等待它们。 `Task.WaitAll` 和 `Task.WaitAny` 阻断激活的调度器。

耐久的组合将显示组合体名称下的每一输入,例如 `reserve/0` 和 `reserve/1`,将输入排成一个批次,一份日记承诺这样做,记录哪些输入先完成,因此每个重放都观察相同结果。

组合体见 [组合器和控点](../grains/durable-tasks.md#combinators-and-handles)。

<a id="how-to-fix-violations"></a>

## 如何纠正违反情况

**使用 `DurableTask.WhenAll`** 作为结果输入顺序:

:::code source="snippets/durable-tasks/Orleans0210.cs" id="orleans0210_fix_when_all" language="csharp":::

**使用 `DurableTask.Race`** 来进行第一个结果。 `Race` 请求取消其他输入,等待每个输入为终端,所以不使用输入的寿命超过它。 `DurableTask.WhenAny` 返回第一个结果,剩下的运行时间为:

:::code source="snippets/durable-tasks/Orleans0210.cs" id="orleans0210_fix_race" language="csharp":::

**使用 `DurableTask.ScheduleAll`** 启动每个输入并用其控管继续输入,并且 `DurableTask.WhenAllCompleted` 不扔弃等待每一个结果。要将耐久工作捆绑在一起,不要与 `Task.Delay`赛跑,而使用任务 `WaitAsync` 或 `WaitUntilAsync`。

<a id="when-to-suppress"></a>

## 何时压缩

当合并任务没有执行耐久操作时, 无法进行诊断, 分析师无法确定这一点, 例如当他们呼叫其他组装的助手时。
