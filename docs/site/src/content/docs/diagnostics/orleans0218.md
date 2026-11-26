---
title: "ORLEANS0218:通过常数版本名称和范围"
description: "理解 ORLEANS0218, 哪个报告版本检查时其更改名称或支持范围在运行时间计算, 并用常数或 DurableVersion 描述符来修正。"
ms.date: 10/19/2026
ms.topic: reference
---

<a id="orleans0218-pass-constant-version-names-and-ranges"></a>

# ORLEANS0218:通过常数版本名称和范围

| 财产 | 价值值值 |
| --- | --- |
| 严重性 | 警告 |
| 类别类 | 用法 |
| 代码修正 | 无无无无 |

<a id="cause"></a>

## 原因原因原因

`DurableTask.GetVersion` 或 `DurableVersion` 构建器收到一个在运行时计算的名称、 `minSupported`或 `maxSupported` 参数,例如内插字符串或参数:

:::code source="snippets/durable-tasks/Orleans0218.cs" id="orleans0218_violation" language="csharp":::

<a id="rule-description"></a>

## 规则说明

执行记录中更改的第一个检查, 以及每次后来的检查, 包括每部重放, 都读回了同一名称下的记录版本。 执行分支遵循该版本, 所以每个重播都计划第一个运行的操作 。

以数据创建的名称为每个值的变化。当数据在运行和重放之间有差异,或者如果部署建立不同的名称时,重播检查执行从未记录过的名字、记录第二个决定,并且可以取另一个分支而不是用于计划记录的操作的运行。重播则会有所不同。

调用代码的版本所在区域状态: `maxSupported` 是该代码执行的最新版本, `minSupported` 也是它仍然重播的最老版本。 两者都属于版本中分支的代码。 调用者通过的范围可以接受一个代码没有分支的版本, 或者退出一个在等待处决之前记录下来的版本, 拖住它们。

版本见 [版本耐久任务](../grains/durable-tasks-versioning.md)。

<a id="how-to-fix-violations"></a>

## 如何纠正违反情况

**传递常数。** 用一个常数来命名更改, 并指定分支执行的范围。 选择行为的数据, 如区域, 转到每个分支的操作 :

:::code source="snippets/durable-tasks/Orleans0218.cs" id="orleans0218_fix_constant" language="csharp":::

**声明 `DurableVersion`。** 一个静态描述符一次说明更改的名称和范围,而身体读为 `Get` 或 `IsAtLeast`:

:::code source="snippets/durable-tasks/Orleans0218.cs" id="orleans0218_fix_descriptor" language="csharp":::

<a id="when-to-suppress"></a>

## 何时压缩

禁止对每次检查时的一生中始终保持不变的价值进行诊断,例如通过 `static readonly` 字段生成的固定类供应的名称。
