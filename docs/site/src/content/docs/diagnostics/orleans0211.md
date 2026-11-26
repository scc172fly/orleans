---
title: "ORLEANS0211:等待时间拖"
description: "理解 ORLEANS0211,其中报告 Task.Delay 或 Thread.Sleep 的耐久任务代码,并用 DurableTask.Delay、 DurableTask.DelayUntil或持久超时来修正。"
ms.date: 10/15/2026
ms.topic: reference
---

<a id="orleans0211-wait-with-durable-delays"></a>

# ORLEANS0211:等待时间拖

| 财产 | 价值值值 |
| --- | --- |
| 严重性 | 警告 |
| 类别类 | 用法 |
| 代码修正 | 无无无无 |

<a id="cause"></a>

## 原因原因原因

持久任务机构在仍然能够开展持久行动时等待 `Task.Delay` 或呼叫 `Thread.Sleep` :

:::code source="snippets/durable-tasks/Orleans0211.cs" id="orleans0211_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

`Task.Delay` 等待当前运行的记忆。 当激活停止、 其 silo 重新启动或运行在延迟结束前失败时, 等待丢失, 重播开始14天的新等待从运行开始, 因此每几天重新开始一次的试验永远不会结束。 `Thread.Sleep` 还阻断了启动的调度器, 所以 grain 在它睡觉的时候不处理其它问题 。

长期延误记录了执行时的正常时间。 grain 可以在等待期间停止使用,运行时间在延迟到期时重新启动,重播恢复了记录的延误而不是开始另一个延迟。

关于延误和超时,见 [组合器和控点](../grains/durable-tasks.md#combinators-and-handles)。

<a id="how-to-fix-violations"></a>

## 如何纠正违反情况

**等待 `DurableTask.Delay`:**

:::code source="snippets/durable-tasks/Orleans0211.cs" id="orleans0211_fix_delay" language="csharp":::

**等待到有 `DurableTask.DelayUntil`的时间。** 从参数、记录的结果或 `DurableTask.Current.UtcNow`计算时间, 所以每次重编都同时计算到期时间:

:::code source="snippets/durable-tasks/Orleans0211.cs" id="orleans0211_fix_delay_until" language="csharp":::

**与 `WaitAsync` 或 `WaitUntilAsync`或** 的工作相隔绝,而不是与 `Task.Delay`进行赛跑。超时是从第一次运行起计数的持久延迟。当工作开始后,会收到取消请求,而结束的任务则取消丢弃 `TimeoutException`:

:::code source="snippets/durable-tasks/Orleans0211.cs" id="orleans0211_fix_timeout" language="csharp":::

<a id="when-to-suppress"></a>

## 何时压缩

将诊断限制为短暂停的诊断, 重放可能会重复一次, 例如在持续操作前暂停, 以测量资源仍在启动中。 在外部呼叫后出现回击 `DurableTask.Run` 代表不能持久运作 所以规则不会报告
