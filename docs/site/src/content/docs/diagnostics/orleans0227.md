---
title: "ORLEANS0227: 在 DurableTask.RunInline 效果之外运行持久化操作"
description: 了解 ORLEANS0227，它报告在 DurableTask.RunInline 效果内部启动任务或检查版本的行为，这些行为会引发异常。应通过在任务主体中运行这些操作进行修复。
ms.date: 10/19/2026
ms.topic: reference
---

<a id="orleans0227-run-durable-operations-outside-durabletaskruninline-effects"></a>

# ORLEANS0227: 在 DurableTask.RunInline 效果之外运行持久化操作

| 属性 | 值 |
| --- | --- |
| 严重性 | 错误 |
| 类别 | 用法 |
| 代码修复 | 无 |

<a id="cause"></a>

## 原因

`DurableTask.RunInline` 效果的委托通过 `ScheduleAsync` 或 `Post` 启动持久化任务，或者通过 `DurableTask.GetVersion`、`DurableVersion.Get` 或 `DurableVersion.IsAtLeast` 检查版本：

:::code source="snippets/durable-tasks/Orleans0227.cs" id="orleans0227_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

重放返回效果已记录的结果，而不运行效果本身。效果中的持久化操作只会在首次运行时执行：重放将跳过该操作，主体后续操作的标识符也会发生偏移。

对于效果内部的持久化操作，运行时会抛出 `InvalidOperationException`，效果会将该异常记录为自身的结果。效果在抛出异常之前所做的变更与已记录的异常一起提交。在示例中，座位数量递增后版本检查抛出异常，该递增操作仍会提交；主体的每次运行（包括每次重放）都会从 `DurableTask.RunInline` 收到已记录的异常。

效果内部的 `DurableTask.RunInline` 调用作为外层效果的一部分运行，其结果也属于外层效果结果的一部分。

有关效果，请参阅[效果与步骤](../grains/durable-tasks.md#effects-and-steps)。

<a id="how-to-fix-violations"></a>

## 如何修复违规

**在主体中运行操作。** 在效果之前或之后检查版本并启动任务。通过效果的闭包传入值，并从效果返回值：

:::code source="snippets/durable-tasks/Orleans0227.cs" id="orleans0227_fix" language="csharp":::

<a id="when-to-suppress"></a>

## 何时抑制诊断

请保持此诊断启用：效果内部的持久化操作会抛出 `InvalidOperationException`，效果会将其记录为自身的结果。
