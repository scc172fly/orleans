---
title: "ORLEANS0223: 为 Grain 状态赋予新值，而不是就地修改"
description: 了解 ORLEANS0223，它报告持久化任务代码就地修改日志化 Grain 状态所持有对象的情况，并介绍如何使用不可变值及赋值操作修复。
ms.date: 10/19/2026
ms.topic: reference
---

<a id="orleans0223-assign-new-values-to-grain-state-instead-of-changing-them-in-place"></a>

# ORLEANS0223: 为 Grain 状态赋予新值，而不是就地修改

| 属性 | 值 |
| --- | --- |
| 严重性 | 警告 |
| 类别 | 用法 |
| 代码修复 | 无 |

<a id="cause"></a>

## 原因

持久化任务代码就地修改 Grain 状态持有的对象：为其成员或元素赋值、递增或递减，或者调用会修改集合的方法（例如 `Add` 或 `Clear`）。该对象直接通过 `IDurableValue<T>.Value`、`IPersistentState<T>.State` 或持久化字典或列表的条目访问，也可以通过保存该对象的局部变量间接访问：

:::code source="snippets/durable-tasks/Orleans0223.cs" id="orleans0223_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

持久化值和集合会记录经其 API 进行的每次变更，例如为 `IDurableValue<T>.Value` 或持久化字典的条目赋值。变更随 Grain 的下一次日志写入提交。

就地修改这些按操作跟踪变更的值或集合条目持有的对象，会绕过 API。内存中的对象包含该变更，但在另一项变更写入整个值之前，没有任何记录包含它。在示例中，任务已完成且其结果已提交，但日志中的心愿列表没有新增条目。从日志恢复 Grain 的激活实例将丢失该条目。

由日志支持的 `IPersistentState<T>` 在每次日志写入时记录完整状态，包括序列化成员和集合的就地变更。对于持久化任务代码中的此类状态，此规则也建议使用不可变值和赋值操作。

值类型、成员仅允许初始化赋值的记录类型，以及不可变集合通过赋值操作进行变更，API 会记录这些操作，因此规则不会报告它们。

有关重放安全的状态，请参阅[重放安全规则](../grains/durable-tasks-replay-rules.md#state-values-and-external-work)。

<a id="how-to-fix-violations"></a>

## 如何修复违规

**存储不可变值，并赋予新值。** 包含不可变集合的记录类型返回修改后的副本，赋值操作将其记录下来：

:::code source="snippets/durable-tasks/Orleans0223.cs" id="orleans0223_fix" language="csharp":::

<a id="when-to-suppress"></a>

## 何时抑制诊断

如果状态仅在内存中持有某个成员（例如序列化器跳过的缓存），则可抑制此诊断。
