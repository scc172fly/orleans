---
title: "ORLEANS0200: 在已记录效果中或最后一次持久化操作之后访问 Grain 状态"
description: 了解 ORLEANS0200：持久化任务代码在重放时会再次执行的位置读取或修改 Grain 状态。使用已记录效果、同步尾部或本地步骤修复。
ms.date: 10/15/2026
ms.topic: reference
---

<a id="orleans0200-access-grain-state-in-recorded-effects-or-after-the-last-durable-operation"></a>

# ORLEANS0200: 在已记录效果中或最后一次持久化操作之后访问 Grain 状态

| 属性 | 值 |
| --- | --- |
| 严重性 | 警告 |
| 类别 | 用法 |
| 代码修复 | 无 |

<a id="cause"></a>

## 原因

持久化任务主体在之后仍会到达持久化操作的位置读取或修改 Grain 状态。Grain 状态指类型为或实现了日志化集合（`IDurableValue<T>`、`IDurableDictionary<TKey, TValue>`、`IDurableList<T>`、`IDurableQueue<T>`、`IDurableSet<T>`、`IDurableTaskCompletionSource<T>` 或 `IDurableLog<T>`）或持久化状态（`IPersistentState<TState>`）的字段或属性。第一个方法在发送提醒前计数，第二个方法依据一个被自身步骤修改的状态进行分支：

:::code source="snippets/durable-tasks/Orleans0200.cs" id="orleans0200_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

重放从头执行任务主体，每个已记录操作返回其已记录的结果。Grain 状态不会重新构建：它保有全部已提交的变更，包括后续操作所作的变更。因此，主体最后一次持久化操作之前的代码会针对更新后的状态再次执行：

- **写入再次生效。** 停用后，重放 `RemindAsync` 会再次递增 `_remindersSent`，尽管提醒只发送了一次。
- **读取观察到后续写入。** 重放 `ShipWhenPaidAsync` 时会读到 `Shipped`，即已记录的 `ship` 步骤所写入的状态，并跳过首次运行进入的分支。如果重放调度不同的操作，或者以不同参数调度相同操作，就会返回不同的结果，或出现分歧并使执行停滞。

以下三个位置只访问一次 Grain 状态：

- `DurableTask.RunInline` 效果执行一次，其状态变更与记录一同提交。
- 本地步骤，即同一 Grain 上的 `async DurableTask` 方法，将其最后一段代码的状态变更与已记录结果一同提交。
- 主体的同步尾部，即最后一次持久化操作之后的代码，将其状态变更与任务结果一同提交。

有关规则与重放模型，请参阅[重放安全规则](../grains/durable-tasks-replay-rules.md)。

<a id="how-to-fix-violations"></a>

## 如何修复违规

**使用 `DurableTask.RunInline` 记录访问。** 效果执行一次并返回其输出，重放返回已记录的值。同样记录用于决定后续操作的状态读取；将进度追加到 `IDurableLog<T>` 时使用 `DurableTask.RunInline(() => log.Append(entry))`，确保每个条目只追加一次：

:::code source="snippets/durable-tasks/Orleans0200.cs" id="orleans0200_fix_once" language="csharp":::

**将变更移到同步尾部。** 如果变更之后没有持久化操作，则该变更与任务结果一同提交：

:::code source="snippets/durable-tasks/Orleans0200.cs" id="orleans0200_fix_tail" language="csharp":::

**在本地步骤中执行变更。** 主体修改状态后返回的步骤（如上述 `MarkShipped`）将变更与已记录结果一同提交，重放直接返回结果而不执行步骤：

:::code source="snippets/durable-tasks/Orleans0200.cs" id="orleans0200_fix_step" language="csharp":::

有关已记录效果，请参阅[已记录效果、本地步骤及对其他 Grain 的调用](../grains/durable-tasks.md#recorded-effects-local-steps-and-calls-to-other-grains)。

<a id="when-to-suppress"></a>

## 何时抑制

可以对不决定持久化操作、其参数或状态变更的读取抑制诊断，例如值仅用于日志消息；也可对任务运行期间不会被任何操作修改的状态抑制，例如 Grain 创建时只写入一次的值。
