---
title: "ORLEANS0202: 在步骤中或最后一次持久化操作之后等待外部操作"
description: 了解 ORLEANS0202：持久化任务代码在重放时会再次执行的位置等待服务、Grain 调用或其他外部工作。使用步骤或同步尾部修复。
ms.date: 10/15/2026
ms.topic: reference
---

<a id="orleans0202-await-external-operations-in-steps-or-after-the-last-durable-operation"></a>

# ORLEANS0202: 在步骤中或最后一次持久化操作之后等待外部操作

| 属性 | 值 |
| --- | --- |
| 严重性 | 警告 |
| 类别 | 用法 |
| 代码修复 | 无 |

<a id="cause"></a>

## 原因

持久化任务主体在之后仍会到达持久化操作的位置等待非持久化操作，例如 `HttpClient` 调用、数据库查询、普通 Grain 调用或返回 `Task` 的服务方法。等待持久化任务及其配置和句柄、持久化任务 API、`Task.Yield`，以及同一编译单元中执行持久化操作的辅助方法，不属于外部等待。以下发票流程在第一个步骤之前获取汇率：

:::code source="snippets/durable-tasks/Orleans0202.cs" id="orleans0202_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

重放会再次执行主体最后一次持久化操作之前的代码，包括其中每一次等待。外部等待因而可能：

- **重复产生效果。** 修改其他系统的调用（例如发送消息或创建记录）在每次重放中都会执行。
- **观察到不同结果。** 重放收到另一个汇率，并向已记录的 `bill` 步骤传入另一个金额。如果该步骤是对另一个 Grain 的调用，执行会出现分歧并停滞；否则会返回针对不同金额的已记录总额。
- **在首次运行成功的位置失败。** 如果重放期间服务不可用，任务会失败，即使所有已记录操作均成功。

步骤会记录结果，因此重放无需再次等待操作即可返回结果。

有关规则与重放模型，请参阅[重放安全规则](../grains/durable-tasks-replay-rules.md)。

<a id="how-to-fix-violations"></a>

## 如何修复违规

**在 `DurableTask.Run` 中等待操作。** 委托作为子步骤运行，其结果被记录；步骤接收一个由持久化取消发出信号的取消令牌：

:::code source="snippets/durable-tasks/Orleans0202.cs" id="orleans0202_fix_run" language="csharp":::

**在本地步骤中等待操作。** 同一 Grain 的 `async DurableTask` 方法会记录其完成情况。如果步骤执行多项工作，或其他工作流会复用它，则为其命名：

:::code source="snippets/durable-tasks/Orleans0202.cs" id="orleans0202_fix_step" language="csharp":::

**在主体最后一次持久化操作之后等待操作。** 同步尾部仅在完成主体的运行中执行：

:::code source="snippets/durable-tasks/Orleans0202.cs" id="orleans0202_fix_tail" language="csharp":::

在结果提交前结束的步骤会再次运行；在任务结果提交前结束的运行，其尾部也会再次运行。因此步骤或尾部中的外部操作至少运行一次；当它修改其他系统时，应赋予它从任务派生的幂等键，例如 `$"{grainId}/{DurableTask.Current.Id}/{DurableTask.Current.Incarnation:N}"`。参阅[持久化任务中的幂等性](../grains/durable-tasks-idempotency.md)和[将持久化任务与其他系统集成](../grains/durable-tasks-integrations.md)。运行世代标识区分使用同一任务标识符的后续运行，其操作属于新的操作。

<a id="when-to-suppress"></a>

## 何时抑制

对于幂等、每次运行返回相同值且重放期间不会失败的读取，可以抑制诊断，例如读取不可变配置。
