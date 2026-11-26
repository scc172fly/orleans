---
title: "ORLEANS0220:列出职位可能改变的持久作业"
description: "理解 ORLEANS0220,其中在循环、条件或其他未命名作业之后报告不命名的持久操作,并用 WithId 或 DurableTask.RunInline 的名称来修正。"
ms.date: 10/19/2026
ms.topic: reference
---

<a id="orleans0220-name-durable-operations-whose-position-can-change"></a>

# ORLEANS0220:列出职位可能改变的持久作业

| 财产 | 价值值值 |
| --- | --- |
| 严重性 | 信息信息 |
| 类别类 | 用法 |
| 代码修正 | 可用 |

<a id="cause"></a>

## 原因原因原因

长期任务机构在循环或条件中或在另一类未命名的行动之后,运行一个名称不明的持久操作。 无名操作是指该机构等待、计划或没有 `WithId`、无姓名效果 `DurableTask.RunInline` 效应的职位,以及等待的不使用 `WithId`的组合器:

:::code source="snippets/durable-tasks/Orleans0220.cs" id="orleans0220_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

无名操作使用执行后下一个生成的识别符号, 它将计算出它之前运行的未命名操作。 儿童、 效果和分离开始时共享一个数, `$child-1`、 `$child-2`等, 等待的组合器会从自己的计数中获取操作标识符, 如 `$when-all-1`。 身体在直接等待的等待信号等待作为执行的一部分, 没有识别符号 。

例如,点效果是执行重复命令的 `$child-1` 和执行第一命令的 `$child-2` 。在同一代码上重播同样的执行,重复了该数。在改变计数之前增加、删除或移动一个未命名的行动的部署,因此重放将一项行动安排在一个标识下进行,而执行记录在另一个操作中。重放差异:主机取消了与 `DurableTaskAttemptCancellationReason.Diverged` 的尝试并保持任务不变。

以其呼叫站点识别操作的名称。一个在一个运行中不止一次的呼叫站点,如循环运行的一个,保留一个名字,以后发生的情况按顺序 `name$2`、 `name$3`等。

识别符号见 [各种重播和部署之间的稳定](../grains/durable-tasks-identity.md#stability-across-replays-and-deployments),无名效果见 [效果和步骤](../grains/durable-tasks.md#effects-and-steps)。

<a id="how-to-fix-violations"></a>

## 如何纠正违反情况

**给每个操作命名。** 用 `WithId`命名定义和组合器,并将一个名称作为 `DurableTask.RunInline`的第一个参数:

:::code source="snippets/durable-tasks/Orleans0220.cs" id="orleans0220_fix" language="csharp":::

代码修正添加 `WithId` 定义,并用 kbab-case name 来表示定义的方法,而没有它的定义。 `Async` 后缀, 例如 `send-welcome` 用于 `SendWelcome`。它增加了一个数字,例如 `send-welcome-2`,当身体已经使用名称时。Name `DurableTask.RunInline` 手动效果。

名称会更改操作的识别符号。 在处决前使用它们的行动名称尚未执行, 或者在 `DurableTask.GetVersion`上分割更改 。

<a id="when-to-suppress"></a>

## 何时压缩

禁止一个机构在开始处决的代码上完成处决后进行诊断,例如短寿命步骤,而其循环和条件仅取决于其论点和记录的结果。
