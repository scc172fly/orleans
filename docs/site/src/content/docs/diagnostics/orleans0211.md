---
title: "ORLEANS0211: 使用持久化延迟进行等待"
description: 了解 ORLEANS0211：持久化任务代码使用 Task.Delay 或 Thread.Sleep。使用 DurableTask.Delay、DurableTask.DelayUntil 或持久化超时修复。
ms.date: 10/15/2026
ms.topic: reference
---

<a id="orleans0211-wait-with-durable-delays"></a>

# ORLEANS0211: 使用持久化延迟进行等待

| 属性 | 值 |
| --- | --- |
| 严重性 | 警告 |
| 类别 | 用法 |
| 代码修复 | 无 |

<a id="cause"></a>

## 原因

持久化任务主体在之后仍会到达持久化操作的位置等待 `Task.Delay` 或调用 `Thread.Sleep`：

:::code source="snippets/durable-tasks/Orleans0211.cs" id="orleans0211_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

`Task.Delay` 在当前运行的内存中等待。当激活实例停用、所属 Silo 重启，或运行在延迟结束前失败时，等待会丢失，重放从运行起点开始新的 14 天等待，因此每隔几天重启一次运行的试用期永远不会到期。`Thread.Sleep` 还会阻塞激活实例的调度器，因此 Grain 在休眠期间不处理其他工作。

持久化延迟在执行的逻辑时间中记录到期时间。Grain 可以在等待时停用；延迟到期时，运行时重新激活 Grain，重放恢复已记录的延迟，而不是开始另一次延迟。

有关延迟及超时，请参阅[组合器和句柄](../grains/durable-tasks.md#combinators-and-handles)。

<a id="how-to-fix-violations"></a>

## 如何修复违规

**使用 `DurableTask.Delay` 等待一段时长：**

:::code source="snippets/durable-tasks/Orleans0211.cs" id="orleans0211_fix_delay" language="csharp":::

**使用 `DurableTask.DelayUntil` 等待至指定时间。** 根据参数、已记录结果或 `DurableTask.Current.UtcNow` 计算时间，使每次重放都计算出相同的到期时间：

:::code source="snippets/durable-tasks/Orleans0211.cs" id="orleans0211_fix_delay_until" language="csharp":::

**使用 `WaitAsync` 或 `WaitUntilAsync` 为工作设置时间限制**，而不是与 `Task.Delay` 竞速。超时是从首次运行起计算的持久化延迟。如果超时先到期，则工作收到取消请求，最终处于已取消状态的任务会抛出 `TimeoutException`：

:::code source="snippets/durable-tasks/Orleans0211.cs" id="orleans0211_fix_timeout" language="csharp":::

<a id="when-to-suppress"></a>

## 何时抑制

对于重放可以重复的短暂停顿，可以抑制诊断，例如在轮询仍在启动的资源的持久化操作之前暂停。`DurableTask.Run` 委托内部外部调用各次尝试之间的退避之后不会到达持久化操作，因此此规则不会报告它。
