---
title: "ORLEANS0013:从 grain 类中删除序列化身份属性"
description: "理解和决心 ORLEANS0013 时 grain 执行中标记数据序列化。"
ms.date: 08/25/2026
ms.topic: reference
---

<a id="orleans0013-remove-serialization-identity-attributes-from-a-grain-class"></a>

# ORLEANS0013:从 grain 类中删除序列化身份属性

| 财产 | 价值值值 |
| --- | --- |
| 类别类 | 用法 |
| 严重性 | 错误错误 |
| 代码修正 | 可用 |

<a id="cause"></a>

## 原因原因原因

源自 <xref:Orleans.Grain>,包括 `Grain<TState>`,则标记了 `[Alias]` 或 `[GenerateSerializer]`.

<a id="impact"></a>

## 影响影响

Grain 实施实例是激活物体,而不是应用有效载荷。执行的顺序安排引入了错误的数据合同,并可能造成别名冲突。

<a id="how-to-fix"></a>

## 如何修补

删除删除 `[Alias]` 和 `[GenerateSerializer]` 从 grain 应用序列属性 grain 替换状态和电文类型。

代码修正删除含有无效属性的属性列表。 当同一属性列表包含不相关的属性时, 请审查 diff 。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

仅禁止非常规一体化,这种融合有意将该类人与 Orleans 激活分开连续排列。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0013.severity = none
```
