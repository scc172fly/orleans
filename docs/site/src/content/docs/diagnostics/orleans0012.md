---
title: "ORLEANS0012:更改重复的序列编号d"
description: "理解和决心 ORLEANS0012 成员再使用 Orleans 字段编号。"
ms.date: 08/25/2026
ms.topic: reference
---

<a id="orleans0012-change-a-duplicated-serialization-id"></a>

# ORLEANS0012:更改重复的序列编号d

| 财产 | 价值值值 |
| --- | --- |
| 类别类 | 用法 |
| 严重性 | 错误错误 |
| 代码修正 | 可用 |

<a id="cause"></a>

## 原因原因原因

一种 `[GenerateSerializer]` 类型的两个或多个成员使用相同的常数 `[Id]` 值。

<a id="impact"></a>

## 影响影响

实地识别模糊性可以将数据分解为错误成员、腐败的有效载荷和断裂版本容忍度。

<a id="how-to-fix"></a>

## 如何修补

恢复已建立的字段ID映射。 只为新会员指派一个未使用的ID, 并且不重编已经部署的ID 。

代码修正选择一个大于文档中发现的最高代号的值。在应用“全部固定”之前仔细审查结果。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

不为 Orleans所串联的类型进行此诊断。如果序列说明类型不是 Orleans schema的一部分,则删除该符号。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0012.severity = none
```
