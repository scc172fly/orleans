---
title: "ORLEANS0004:添加缺失的序列属性"
description: "当生成的序列器类型有无注释成员时, 理解并解决 ORLEANS0004 。"
ms.date: 08/25/2026
ms.topic: reference
---

<a id="orleans0004-add-missing-serialization-attributes"></a>

# ORLEANS0004:添加缺失的序列属性

| 财产 | 价值值值 |
| --- | --- |
| 类别类 | 用法 |
| 严重性 | 错误错误 |
| 代码修正 | 可用 |

<a id="cause"></a>

## 原因原因原因

标记的类、支架或记录 `[GenerateSerializer]` 有一个例字段或自动财产,没有 `[Id]` 或 `[NonSerialized]`。

<a id="impact"></a>

## 影响影响

无标识成员可从 Orleans 序列中省略,产生缺省或损失的值和不完整的耐版性计划。

<a id="how-to-fix"></a>

## 如何修补

给有序列的成员指定稳定、独特的 `[Id(n)]` 值。标记被故意排除的 `[NonSerialized]`成员。

代码修正可以在现有最高身份识别符之后添加顺序标识,或者将所有未加注的成员标记为非序列化。 在使用这些身份证明之前先审查生成了身份证明,而不再重新编号已部署的字段。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

优 `[NonSerialized]` 仅对试验装置或明确要求遗漏的遗留类型加以禁止。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0004.severity = none
```
