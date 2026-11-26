---
title: "ORLEANS0216: grains 或持久任务机构的长期任务后的任务"
description: "理解 ORLEANS0216,其中报告代码,在没有耐用发件箱可以启动的持久任务中,该代码会发布一个长期的任务,并用 ScheduleAsync 或从耐用 grain中找到一个员额来解决这个问题。"
ms.date: 10/15/2026
ms.topic: reference
---

<a id="orleans0216-post-durable-tasks-from-grains-or-durable-task-bodies"></a>

# ORLEANS0216: grains 或持久任务机构的长期任务后的任务

| 财产 | 价值值值 |
| --- | --- |
| 严重性 | 警告 |
| 类别类 | 用法 |
| 代码修正 | 无无无无 |

<a id="cause"></a>

## 原因原因原因

在 grain 之外和在持久任务机构外,如客户或网络端点中,代码要求 `Post` 确定耐久的任务定义或配置:

:::code source="snippets/durable-tasks/Orleans0216.cs" id="orleans0216_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

`Post` 同步: 它在耐用发件箱中开始 grain 运行调用代码的调用代码, 并以此开始承诺 grain下一期日记的写作, 以及调用代码所修改的状态。 A grain 方法、耐久任务机构以及 `DurableTask.Run` 代表在 grain客户没有耐久的发信箱,所以 `Post` 扔弃 `InvalidOperationException` 在那里,结账失败了。

任务开始方式见 [开始和观察任务](../grains/durable-tasks.md#starting-and-observing-tasks)。

<a id="how-to-fix-violations"></a>

## 如何纠正违反情况

**任务安排 `ScheduleAsync`.** 任务完成后,它就会完成 grain 已经接受并保存了任务,返回任务控件。 `WithId`,因此在模棱两可的失败(如超时)发生后重试,达到同样的任务:

:::code source="snippets/durable-tasks/Orleans0216.cs" id="orleans0216_fix_schedule" language="csharp":::

**员额的空缺 grain.** 耐久性的一般方法 grain 以原子方式对 grain下一期日记的状态变化:

:::code source="snippets/durable-tasks/Orleans0216.cs" id="orleans0216_fix_grain" language="csharp":::

<a id="when-to-suppress"></a>

## 何时压缩

禁止仅 grains 或持久任务机构所呼唤的辅助人员诊断,例如工作流程用于启动后续工作的静态方法。
