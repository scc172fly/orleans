---
title: "ORLEANS0201:在记录效果中产生非确定值"
description: "理解 ORLEANS0201, 其中报告长任务代码, 该代码生成时钟、 随机或环境值, 而重播则再次运行该代码, 并用 DurableTask.RunInline、 逻辑时间或基于任务的密钥来修正它 。"
ms.date: 10/15/2026
ms.topic: reference
---

<a id="orleans0201-produce-non-deterministic-values-in-recorded-effects"></a>

# ORLEANS0201:在记录效果中产生非确定值

| 财产 | 价值值值 |
| --- | --- |
| 严重性 | 警告 |
| 类别类 | 用法 |
| 代码修正 | 无无无无 |

<a id="cause"></a>

## 原因原因原因

长期任务机构产生的价值在运行时间仍然可以持久运作时,其价值不同。`DateTime.Now`, `DateTime.UtcNow`, `DateTime.Today`, `DateTimeOffset.Now`, `DateTimeOffset.UtcNow`, `TimeProvider.GetUtcNow`, `TimeProvider.GetLocalNow`, `TimeProvider.GetTimestamp`,和 `Stopwatch`),随机值和识别符号(`Guid.NewGuid`, `Guid.CreateVersion7`, `Random`,和 `RandomNumberGenerator`),和进程值(`Environment.TickCount`, `Environment.MachineName`, `Environment.ProcessId`, `Environment.CurrentManagedThreadId`, `Environment.ProcessorCount`,以及环境变量。仅达到日志调用值的值将被免责。

下列付款提取了应急钥匙,并从系统时钟计算最后期限:

:::code source="snippets/durable-tasks/Orleans0201.cs" id="orleans0201_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

重播从一开始就运行身体, 因此它产生另一个值 :

- **另一条最后期限。** 重放从当前时钟计算一个截止时间。 记录的等待会保留其记录的适当时间, 所以机构计算的价值与计划的时间不符 。
- **另一把钥匙** 重放到 `charge` 之后它记录的结果会用新的钥匙 来跳过这一步, 这样付款提供人就再也认不出 重新试和收费了。
- **差异。** 一个达到耐久调用参数的值、一个 `WithId` 名称或分支使重播时间表与第一次记录时不同的操作,执行会分歧和拖拉,直到与记录相匹配的代码重放它为止。

规则和重放模型见 [重演安全规则](../grains/durable-tasks-replay-rules.md)。

<a id="how-to-fix-violations"></a>

## 如何纠正违反情况

**从任务身份中衍生的识别符号, 并读取逻辑时间。** grain、 `DurableTask.Current.Id`和 `DurableTask.Current.Incarnation` 在每个重放中命名运行, 因此在每次重播和尝试中, 以它们为基础的键都是相同的, 包括重复一项指控而结果没有发生的。 内化区分了同一任务标识的后期运行, 例如从早期运行完成之后开始的运行, 历史被删除, 所以在另一个密钥下运行收费。 `DurableTask.Current.UtcNow` 是执行的逻辑时间, 运行时间记录, 所以重编计算同样的截止时间 。 见 [任务产生的衍生的不合格关键](../grains/durable-tasks-idempotency.md#derive-idempotency-keys-from-the-task):

:::code source="snippets/durable-tasks/Orleans0201.cs" id="orleans0201_fix" language="csharp":::

**记录其他值,并使用 `DurableTask.RunInline`。** 效果运行一次,例如 `DurableTask.RunInline("reference", () => Guid.NewGuid().ToString("N"))`,每次重放都返回所记录的值。记录承诺在任务下写下一个日记时加上身体变化的 grain 状态,这样每个运行中,状态和分支都遵守同样的数值。一个留下该写入之前的 grain 值,如一步步通过外部服务的键,来自任务的身份。

**在一个步骤内生成值。** A `DurableTask.Run` 代表或本地步记录其结果,因此它返回的数值随此过程记录下来。

<a id="when-to-suppress"></a>

## 何时压缩

抑制对从未达到持久操作值的诊断、其参数、名称、分支或 grain 状态,例如只测量当前运行时间的时戳,以测量某一度量。
