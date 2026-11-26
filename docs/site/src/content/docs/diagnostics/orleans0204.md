---
title: "ORLEANS0204:返回效果 DurableTask.RunInline 的输出"
description: "理解 ORLEANS0204, 其中报告一个 DurableTask.RunInline 效果, 指定附加体变量, 并通过返回输出来修正它 。"
ms.date: 10/15/2026
ms.topic: reference
---

<a id="orleans0204-return-the-outputs-of-a-durabletaskruninline-effect"></a>

# ORLEANS0204:返回效果 DurableTask.RunInline 的输出

| 财产 | 价值值值 |
| --- | --- |
| 严重性 | 警告 |
| 类别类 | 用法 |
| 代码修正 | 无无无无 |

<a id="cause"></a>

## 原因原因原因

代表的下列代表: `DurableTask.RunInline` 接收指定、加薪或减少一个变量,附后的机构声明:

:::code source="snippets/durable-tasks/Orleans0204.cs" id="orleans0204_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

`DurableTask.RunInline` 运行任务时, 并记录效果结果 。 重放返回所录的结果而不运行效果, 所以代表的指派只在运行它的时候发生。 每次重播中, 都返回已录制的结果, 并且 `number` 居留 `0` 和 `reference` 居留 `Guid.Empty`:重放将其他参数传到所记录的 `print` 并返回比计算的第一个运行过程还多的一张票。

效果所作的状态变化,例如 `_lastNumber`的增量,是正确的:它们与记录一起进行,重放发现它们在 grain状态中。

记录效果见 [记录的效果、当地步骤和调用其他 grains](../grains/durable-tasks.md#recorded-effects-local-steps-and-calls-to-other-grains)。

<a id="how-to-fix-violations"></a>

## 如何纠正违反情况

**返回每个输出。** 将效果的图波还原,并拆除身体内的结构:

:::code source="snippets/durable-tasks/Orleans0204.cs" id="orleans0204_fix" language="csharp":::

**返回记录。** 类型中含有 Orleans 序列器,可以命名输出,而身体可以还原:

:::code source="snippets/durable-tasks/Orleans0204.cs" id="orleans0204_fix_record" language="csharp":::

代表声明的变量,以及其修改的 grain 说明,无需更改。

<a id="when-to-suppress"></a>

## 何时压缩

抑制对身体只在效果内读到的某一任务进行诊断,或仅对在运行过程中产生效果的诊断进行检查。
