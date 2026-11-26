---
title: "ORLEANS0208:给每个持久作业呼叫站自己取名"
description: "了解 ORLEANS0208,其中报告两个持久作业呼叫站位于一个名称相同的机构内,并通过给每个呼叫站点命名来确定。"
ms.date: 10/15/2026
ms.topic: reference
---

<a id="orleans0208-give-each-durable-operation-call-site-its-own-name"></a>

# ORLEANS0208:给每个持久作业呼叫站自己取名

| 财产 | 价值值值 |
| --- | --- |
| 严重性 | 警告 |
| 类别类 | 用法 |
| 代码修正 | 无无无无 |

<a id="cause"></a>

## 原因原因原因

两个呼叫站点 `WithId`,或两个呼叫站点 `DurableTask.RunInline`,在一个持久任务机构里,传递同一常数名称,一个可以同时运行。调用站点位于相互排斥的分支中,比如一个 `if` 语句的两个臂和一个不报告循环重复的呼点。在诸如客户和普通 grain 方法等持久性任务机构之外, `WithId` 点出根任务,规则并不检查:

:::code source="snippets/durable-tasks/Orleans0208.cs" id="orleans0208_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

一个重复运行的重命名, 其按顺序来识别后来发生的情况: 第一个是 `email`, 第二个是 `email$2`。 每个标识符则取决于使用名称的更早呼叫站点数 。

当部署在处决候审时删除欢迎电子邮件时, 重播已经发送两封邮件的处决, 并命名提示电子邮件 `email`。 该标识符保存着欢迎电子邮件的记录 :

- 重播返回欢迎电子邮件的完成记录, 也从未达到小费电子邮件自己的记录 `email$2`。
- 召唤他人 grain,重播在识别符号下发现另一个请求,差异和拖延执行。

当其他呼叫站点被添加、删除或重新排序时,不同的名称保留每个呼叫站点的识别符号。关于标识构建方式,请参见 [任务身份和参考](../grains/durable-tasks-identity.md)。

识别符号和版本,见 [重演安全规则](../grains/durable-tasks-replay-rules.md)。

<a id="how-to-fix-violations"></a>

## 如何纠正违反情况

**给每个呼叫网站自己取名:**

:::code source="snippets/durable-tasks/Orleans0208.cs" id="orleans0208_fix" language="csharp":::

**一次性命名一个重复调用网站。** 一个循环中的调用网站会保留一个名称, 其运行时间数将按顺序排列。 要通过输入来识别每个事件, 请从稳定的值中得出名字, 如 `$"lesson-{index}"`:

:::code source="snippets/durable-tasks/Orleans0208.cs" id="orleans0208_fix_loop" language="csharp":::

<a id="when-to-suppress"></a>

## 何时压缩

停止诊断 当呼叫站点无法同时执行一次执行时 使用分析器无法遵循的条件
