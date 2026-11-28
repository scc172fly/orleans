---
title: "ORLEANS0218: 传入常量版本名称和范围"
description: 了解 ORLEANS0218，它报告在运行时计算变更名称或支持范围的版本检查，并介绍如何使用常量或 DurableVersion 描述符修复。
ms.date: 10/19/2026
ms.topic: reference
---

<a id="orleans0218-pass-constant-version-names-and-ranges"></a>

# ORLEANS0218: 传入常量版本名称和范围

| 属性 | 值 |
| --- | --- |
| 严重性 | 警告 |
| 类别 | 用法 |
| 代码修复 | 无 |

<a id="cause"></a>

## 原因

`DurableTask.GetVersion` 或 `DurableVersion` 构造函数接收到在运行时计算的名称、`minSupported` 或 `maxSupported` 参数，例如内插字符串或方法参数：

:::code source="snippets/durable-tasks/Orleans0218.cs" id="orleans0218_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

一次执行中首次检查某项变更时，会以该变更的名称记录版本。随后每次检查（包括每次重放）都会以同一名称读回已记录的版本。执行的各个分支遵循此版本，因此每次重放都会调度首次运行所调度的操作。

根据数据构造的名称会为每个数据值命名一项变更。如果一次运行与其重放之间的数据不同，或者部署采用不同方式构造名称，重放就会检查一个该执行从未记录过的名称，记录第二个版本决策，并可能进入不同于调度已记录操作的那次运行的分支。此时重放就会发生偏离。

范围声明已部署代码支持哪些版本：`maxSupported` 是代码实现的最新版本，`minSupported` 是代码仍能重放的最早版本。两者都应由根据版本进行分支判断的代码决定。调用方传入的范围可能允许代码中没有相应分支的版本，也可能淘汰尚未完成的执行所记录的版本，导致这些执行停滞。

有关版本控制，请参阅[持久化任务版本控制](../grains/durable-tasks-versioning.md)。

<a id="how-to-fix-violations"></a>

## 如何修复违规

**传入常量。** 使用常量为变更命名，并声明各分支实现的版本范围。用于选择行为的数据（例如区域）应传给各分支中的操作：

:::code source="snippets/durable-tasks/Orleans0218.cs" id="orleans0218_fix_constant" language="csharp":::

**声明 `DurableVersion`。** 静态描述符一次性声明变更名称和范围，任务主体使用 `Get` 或 `IsAtLeast` 读取它：

:::code source="snippets/durable-tasks/Orleans0218.cs" id="orleans0218_fix_descriptor" language="csharp":::

<a id="when-to-suppress"></a>

## 何时抑制诊断

如果某个值在每个检查它的执行的整个生命周期内保持不变，例如由生成的常量类通过 `static readonly` 字段提供的名称，则可抑制此诊断。
