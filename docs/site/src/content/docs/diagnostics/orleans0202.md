---
title: "ORLEANS0202:分阶段或最后一次持久行动之后等待外部业务"
description: "理解 ORLEANS0202, 其中报告等待服务的耐久任务代码、 grain 呼叫或其他外部工作, 重播再次运行时使用该功能, 用步骤或同步尾巴来修正它 。"
ms.date: 10/15/2026
ms.topic: reference
---

<a id="orleans0202-await-external-operations-in-steps-or-after-the-last-durable-operation"></a>

# ORLEANS0202:分阶段或最后一次持久行动之后等待外部业务

| 财产 | 价值值值 |
| --- | --- |
| 严重性 | 警告 |
| 类别类 | 用法 |
| 代码修正 | 无无无无 |

<a id="cause"></a>

## 原因原因原因

长期任务机构等待一项无法持久的行动,例如 `HttpClient` 调用、数据库查询、普通 grain 调用或 `Task`等待长期任务、其配置和处理、持久的任务API, `Task.Yield`,以及从事持久操作的同一编集中的助手不是外部的。以下发票在第一步之前就得到了汇率:

:::code source="snippets/durable-tasks/Orleans0202.cs" id="orleans0202_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

重播在尸体最后一次耐久手术之前播放代码,包括每部都在等待。

- **重复其效果。** 更改另一个系统(如发送信件或创建记录)的呼叫,每次重播一次。
- **观察另一个结果** 重播接收另一种汇率,再通过另一数额入账 `bill` 步骤,当该步骤是呼叫另一个 grain,否则返回以不同数额记录的总额。
- **失败时,第一个运行成功。** 重播在服务不可用时运行,但任务失败,尽管每个记录操作都成功。

一步记录其结果, 所以重放返回结果, 不等待操作再次。

规则和重放模型见 [重演安全规则](../grains/durable-tasks-replay-rules.md)。

<a id="how-to-fix-violations"></a>

## 如何纠正违反情况

**等待 `DurableTask.Run`.** 的操作。代表以儿童步骤行事,结果记录下来,该步骤收到一个吊销标记,其中发出持久的取消信号:

:::code source="snippets/durable-tasks/Orleans0202.cs" id="orleans0202_fix_run" language="csharp":::

**等行动在当地一步。** A 音 A `async DurableTask` 方法的相同 grain 记录完成的完整。 当它做了不止一件事或者当其他工作流程再使用时, 请给它命名 :

:::code source="snippets/durable-tasks/Orleans0202.cs" id="orleans0202_fix_step" language="csharp":::

**尸体最后一次耐久操作后等待手术。** 同步尾巴只在完成身体的运行时运行:

:::code source="snippets/durable-tasks/Orleans0202.cs" id="orleans0202_fix_tail" language="csharp":::

其结果发生之前结束的一步, 其结果被执行前结束的一步会再次运行, 而任务结果被执行之前结束的运行尾尾尾也随之再次运行。 因此, 一个步骤或尾尾部的外部操作至少运行一次, 所以给它一个从任务中产生的应急密钥, 例如 `$"{grainId}/{DurableTask.Current.Id}/{DurableTask.Current.Incarnation:N}"`当它改变另一个系统时;见 [持久任务中具有的](../grains/durable-tasks-idempotency.md) 和 [将耐久任务与其他系统整合](../grains/durable-tasks-integrations.md)。后一种任务识别符的运行是新的,而后一种任务识别符则不同。

<a id="when-to-suppress"></a>

## 何时压缩

抑制读数诊断, 它在每次运行中返回相同值, 并且无法在重放时失败, 比如读取不可变配置 。
