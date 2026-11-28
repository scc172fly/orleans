---
title: "ORLEANS0201: 在已记录效果中生成非确定性值"
description: 了解 ORLEANS0201：持久化任务代码在重放时会再次执行的位置生成时钟、随机或环境值。使用 DurableTask.RunInline、逻辑时间或从任务派生的键修复。
ms.date: 10/15/2026
ms.topic: reference
---

<a id="orleans0201-produce-non-deterministic-values-in-recorded-effects"></a>

# ORLEANS0201: 在已记录效果中生成非确定性值

| 属性 | 值 |
| --- | --- |
| 严重性 | 警告 |
| 类别 | 用法 |
| 代码修复 | 无 |

<a id="cause"></a>

## 原因

持久化任务主体在之后仍会到达持久化操作的位置生成各次运行之间不同的值。此规则报告实际时钟时间（`DateTime.Now`、`DateTime.UtcNow`、`DateTime.Today`、`DateTimeOffset.Now`、`DateTimeOffset.UtcNow`、`TimeProvider.GetUtcNow`、`TimeProvider.GetLocalNow`、`TimeProvider.GetTimestamp` 和 `Stopwatch`）、随机值和标识符（`Guid.NewGuid`、`Guid.CreateVersion7`、`Random` 和 `RandomNumberGenerator`），以及进程值（`Environment.TickCount`、`Environment.MachineName`、`Environment.ProcessId`、`Environment.CurrentManagedThreadId`、`Environment.ProcessorCount` 和环境变量）。仅用于日志调用的值不受此限制。

以下支付流程生成一个幂等键，并根据系统时钟计算截止时间：

:::code source="snippets/durable-tasks/Orleans0201.cs" id="orleans0201_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

重放从头执行主体，因此会生成另一个值：

- **不同的截止时间。** 重放根据当前时钟计算截止时间。已记录等待仍保留其已记录的到期时间，因此主体计算的值与其调度的等待不一致。
- **不同的键。** 在 `charge` 步骤记录结果之前到达该步骤的重放会使用新键执行步骤，支付提供程序因此无法识别重试，并再次扣款。
- **分歧。** 用作持久化调用参数、`WithId` 名称或分支条件的值，可能使重放调度与首次运行记录不同的操作，执行因此出现分歧并停滞，直到与记录匹配的代码重放它。

有关规则与重放模型，请参阅[重放安全规则](../grains/durable-tasks-replay-rules.md)。

<a id="how-to-fix-violations"></a>

## 如何修复违规

**从任务标识派生标识符，并读取逻辑时间。** Grain、`DurableTask.Current.Id` 和 `DurableTask.Current.Incarnation` 在每次重放中均标识同一次运行，因此由它们构造的键在每次重放和尝试中都相同，包括重复执行结果尚未提交的扣款尝试。运行世代标识区分具有相同任务标识符的后续运行，例如此前运行已完成且其历史已删除之后启动的运行，因此后续运行使用不同的键扣款。`DurableTask.Current.UtcNow` 是运行时记录的执行逻辑时间，所以重放会计算相同的截止时间。参阅[从任务派生幂等键](../grains/durable-tasks-idempotency.md#derive-idempotency-keys-from-the-task)：

:::code source="snippets/durable-tasks/Orleans0201.cs" id="orleans0201_fix" language="csharp":::

**使用 `DurableTask.RunInline` 记录其他值。** 效果执行一次（例如 `DurableTask.RunInline("reference", () => Guid.NewGuid().ToString("N"))`），每次重放均返回已记录的值。该记录与主体修改的 Grain 状态一同在任务的下一次日志写入时提交，使各次运行中的状态和分支观察到相同的值。在该次写入前离开 Grain 的值，例如步骤传给外部服务的键，应从任务标识派生。

**在步骤内生成值。** `DurableTask.Run` 委托或本地步骤记录其结果，因此它返回的值会随结果一同记录。

<a id="when-to-suppress"></a>

## 何时抑制

当值不会用于持久化操作、其参数、名称、分支条件或 Grain 状态时，可以抑制此诊断，例如仅用于计量当前运行持续时间的指标时间戳。
