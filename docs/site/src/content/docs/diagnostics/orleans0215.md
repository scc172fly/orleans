---
title: "ORLEANS0215:通过重放安全日志记录器登录"
description: "理解 ORLEANS0215, 其中报告日志用耐久任务代码调用, 重播再次运行, 用 ReplaySafe() 来修正它。"
ms.date: 10/15/2026
ms.topic: reference
---

<a id="orleans0215-log-through-a-replay-safe-logger"></a>

# ORLEANS0215:通过重放安全日志记录器登录

| 财产 | 价值值值 |
| --- | --- |
| 严重性 | 信息信息 |
| 类别类 | 用法 |
| 代码修正 | 无无无无 |

<a id="cause"></a>

## 原因原因原因

耐久任务机构通过 `ILogger`日志,在仍然达到耐久操作的点上,使用 `LoggerExtensions` 方法、 `ILogger.Log`或 `[LoggerMessage]` 方法进行记录,而对账器不是返回 `ReplaySafe()` 的:

:::code source="snippets/durable-tasks/Orleans0215.cs" id="orleans0215_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

重播在机体上次持久操作之前运行代码, 包括日志调用。 在例中导出第十个分区期间的禁用后, 重复播放九个已完成分区的日志 `Exporting partition` , 每次重播任务时重复。 日志随后高写了工作, 以日志为基础的度量和提醒又计数它 。

返回 `ReplaySafe()` 的对帐器在任务重播时跳过事件: 从发现已记录操作的运行开始, 到运行到达一个没有早期记录的操作。 每个事件随后都通过第一个运行记录下来。 当启动在运行写入前结束时, 下次运行重复未承诺的工作及其日志, 因此这些日志适合诊断, 并且完全对应的度量或审计属于记录的结果 。

重播安全记录,见 [重弹和确定论](../grains/durable-tasks.md#replay-and-determinism)。

<a id="how-to-fix-violations"></a>

## 如何纠正违反情况

以 `ReplaySafe()`包扎日志, 通常在 grain的构建器中一次。 日志在日志时读取运行中的任务, 所以普通的 grain 方法也使用它:

:::code source="snippets/durable-tasks/Orleans0215.cs" id="orleans0215_fix" language="csharp":::

以 `DurableTask.RunInline` 效果、一步或连续重复的日志记录,直到结果发生:重放返回已记录的结果,而不运行其代码,在激活结束运行前没有记录的代码和日志再次出现。

<a id="when-to-suppress"></a>

## 何时压缩

禁止对记录每运行一次的日志进行诊断, 例如调试日志显示重放发生地点 。
