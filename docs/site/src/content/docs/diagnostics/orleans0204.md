---
title: "ORLEANS0204: 返回 DurableTask.RunInline 效果的输出"
description: 了解 ORLEANS0204：DurableTask.RunInline 效果对外围主体的变量赋值。通过返回输出来修复。
ms.date: 10/15/2026
ms.topic: reference
---

<a id="orleans0204-return-the-outputs-of-a-durabletaskruninline-effect"></a>

# ORLEANS0204: 返回 DurableTask.RunInline 效果的输出

| 属性 | 值 |
| --- | --- |
| 严重性 | 警告 |
| 类别 | 用法 |
| 代码修复 | 无 |

<a id="cause"></a>

## 原因

`DurableTask.RunInline` 接收的委托对外围主体声明的变量赋值、递增或递减：

:::code source="snippets/durable-tasks/Orleans0204.cs" id="orleans0204_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

`DurableTask.RunInline` 为正在运行的任务执行一次效果，并记录效果结果。重放直接返回已记录结果，而不执行效果，因此委托中的赋值只发生在实际执行效果的运行中。每次重放时，`number` 仍为 `0`，`reference` 仍为 `Guid.Empty`：重放会向已记录的 `print` 步骤传递不同参数，并返回与首次运行计算的不同的票据。

效果所作的状态变更是正确的，例如递增 `_lastNumber`：它们与记录一同提交，重放时可在 Grain 状态中找到这些变更。

有关已记录效果，请参阅[已记录效果、本地步骤及对其他 Grain 的调用](../grains/durable-tasks.md#recorded-effects-local-steps-and-calls-to-other-grains)。

<a id="how-to-fix-violations"></a>

## 如何修复违规

**返回每项输出。** 从效果返回元组，并在主体中解构它：

:::code source="snippets/durable-tasks/Orleans0204.cs" id="orleans0204_fix" language="csharp":::

**返回记录类型。** 使用具备 Orleans 序列化器的类型为各项输出命名，主体可以返回该类型的值：

:::code source="snippets/durable-tasks/Orleans0204.cs" id="orleans0204_fix_record" language="csharp":::

委托自身声明的变量及其修改的 Grain 状态无需更改。

<a id="when-to-suppress"></a>

## 何时抑制

如果主体仅在效果内部读取被赋值的变量，或者仅在执行该效果的运行中将其用于诊断，可以抑制诊断。
