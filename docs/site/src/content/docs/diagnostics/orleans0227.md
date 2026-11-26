---
title: "ORLEANS0227:在 DurableTask.RunInline 效果之外运行耐久操作"
description: "理解 ORLEANS0227, 其中报告任务开始和版本检查在 DurableTask.RunInline 效果中, 投放效果, 并通过运行身体操作来修正它们 。"
ms.date: 10/19/2026
ms.topic: reference
---

<a id="orleans0227-run-durable-operations-outside-durabletaskruninline-effects"></a>

# ORLEANS0227:在 DurableTask.RunInline 效果之外运行耐久操作

| 财产 | 价值值值 |
| --- | --- |
| 严重性 | 错误错误 |
| 类别类 | 用法 |
| 代码修正 | 无无无无 |

<a id="cause"></a>

## 原因原因原因

代表大会 `DurableTask.RunInline` 效果开始一个耐久任务, `ScheduleAsync` 或 `Post`,或检查版本 `DurableTask.GetVersion`, `DurableVersion.Get`,或 `DurableVersion.IsAtLeast`:

:::code source="snippets/durable-tasks/Orleans0227.cs" id="orleans0227_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

重放返回效果记录的结果, 但没有运行效果。 效果的持久操作只在第一时间运行: 重播会错过它, 而身体后来操作的识别器也会转移。

运行时将持续操作的 `InvalidOperationException` 投入效果内,效果记录为例外结果。在抛出之前的效果变化有记录的例外。例如,版本检查在座位数递增后丢弃,加薪和身体的每一次运行,包括每次重播,都从 `DurableTask.RunInline`中获取记录下的例外。

内接 `DurableTask.RunInline` 效应作为附加效果的一部分,其结果作为附件结果的一部分。

效果见 [效果和步骤](../grains/durable-tasks.md#effects-and-steps)。

<a id="how-to-fix-violations"></a>

## 如何纠正违反情况

**运行在正文中的操作。** 检查版本,并在效果前后启动任务。通过关闭将值传递到效果中,并从中返回数值:

:::code source="snippets/durable-tasks/Orleans0227.cs" id="orleans0227_fix" language="csharp":::

<a id="when-to-suppress"></a>

## 何时压缩

保持诊断功能:一个持久的操作效果会产生 `InvalidOperationException`,结果记录为结果。
