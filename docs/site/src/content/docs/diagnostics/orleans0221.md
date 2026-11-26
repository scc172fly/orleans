---
title: "ORLEANS0221:与 DurableTask.RunInline同步运行工作"
description: "理解 ORLEANS0221, 其中报告 DurableTask.Run 步骤只运行同步工作, 并用 DurableTask.RunInline 效果固定这些步骤, 在处决候审期间被 DurableTask.GetVersion 封住。"
ms.date: 10/19/2026
ms.topic: reference
---

<a id="orleans0221-run-synchronous-work-with-durabletaskruninline"></a>

# ORLEANS0221:与 DurableTask.RunInline同步运行工作

| 财产 | 价值值值 |
| --- | --- |
| 严重性 | 信息信息 |
| 类别类 | 用法 |
| 代码修正 | 无无无无 |

<a id="cause"></a>

## 原因原因原因

一个持久任务机构等待着一个 `DurableTask.Run` 步骤,它可以选择给它定名或屏蔽,该步骤的羊羔没有等待任何东西,不返回任何任务,也不读到该步骤收到的 `CancellationToken` :

:::code source="snippets/durable-tasks/Orleans0221.cs" id="orleans0221_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

`DurableTask.Run` 的代表作为儿童处决来管理,在机构等待步骤时,可以留到稍后某个时候,其自己的 `DurableTask.Current` 和取消范围。该步骤以自己写成的日记形式承诺其结果。

`DurableTask.RunInline` 运行当前工作项目的同步工作, 并记录其结果与执行日记的下一期日记的写法, 以及效果产生的状态变化。 控制效果会在同一日记中发生。 在修正时, 状态变化及其记录会随着执行的下一期日记的写法而不是自己写的写法而发生。

在同一呼叫站点,一个步骤和效果使用相同的识别码,而执行记录则使用同样的标识符。重放发现一个记录在尸体现在产生不同效果的一步,因此,在等待处决处部署时, `DurableTask.GetVersion`上的变化。

效果和步骤见 [效果和步骤](../grains/durable-tasks.md#effects-and-steps)。

<a id="how-to-fix-violations"></a>

## 如何纠正违反情况

**运行工作效果。** 将代表传给 `DurableTask.RunInline` ,并注明步骤名称:

:::code source="snippets/durable-tasks/Orleans0221.cs" id="orleans0221_fix" language="csharp":::

**处对版本的修改。** 记录该步骤的处决尚未执行,但尸体对新的处决产生影响,并保留记录在册的处决步骤:

:::code source="snippets/durable-tasks/Orleans0221.cs" id="orleans0221_fix_versioned" language="csharp":::

<a id="when-to-suppress"></a>

## 何时压缩

禁止诊断需要自己处决儿童的步骤,例如,为了取消其范围,以及在记录该步骤重播的处执行。
