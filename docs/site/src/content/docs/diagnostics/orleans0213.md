---
title: "ORLEANS0213:从非同步代码返回长期任务机构的任务"
description: "了解 ORLEANS0213,其中报告方法、地方功能和羊羔在持久任务机构内是无效的,并通过返回和等待任务来加以修正。"
ms.date: 10/15/2026
ms.topic: reference
---

<a id="orleans0213-return-a-task-from-asynchronous-code-in-durable-task-bodies"></a>

# ORLEANS0213:从非同步代码返回长期任务机构的任务

| 财产 | 价值值值 |
| --- | --- |
| 严重性 | 警告 |
| 类别类 | 用法 |
| 代码修正 | 无无无无 |

<a id="cause"></a>

## 原因原因原因

持久任务机构宣布一个 `async void` 本地功能,或一个 `async` 羊羔,转换成返回 `void`的代表,例如 `Action`。 [ORLEANS0222](orleans0222.md) 报告有 `DurableTask.Run` 或 `DurableTask.RunInline` 收到的这种羊羔:

:::code source="snippets/durable-tasks/Orleans0213.cs" id="orleans0213_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

一个持久任务机构观察它等待的行动:结果、失败和取消。 `void` 无法等待代码。 例如:

- 返回物体, 以及任务结果在电子邮件发送前被执行 。
- 不发送任务会从身体中脱逃, 任务就能顺利完成, 而例外则在任务之外出现 。
- 代码在完成身体后第一次 `await` 运行之后,在任务执行之外进行的持久操作。

规则和重放模型见 [重演安全规则](../grains/durable-tasks-replay-rules.md)。

<a id="how-to-fix-violations"></a>

## 如何纠正违反情况

**返回a `DurableTask` 并等待着它,** 身体观察步骤的结果,重播回回录记录:

:::code source="snippets/durable-tasks/Orleans0213.cs" id="orleans0213_fix_step" language="csharp":::

**从回调返回一个 `Task` 。** 使用 `Func<Task>` ,而不是 `Action` ,作为无同步回调,并等待在一步内回调:

:::code source="snippets/durable-tasks/Orleans0213.cs" id="orleans0213_fix_func" language="csharp":::

<a id="when-to-suppress"></a>

## 何时压缩

事件处理者,需要签名 `void` 该机构可以通过其 grain.
