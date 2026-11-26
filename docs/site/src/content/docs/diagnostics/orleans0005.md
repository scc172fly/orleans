---
title: "ORLEANS0005:添加 GenerateSerializer"
description: "理解和决心 ORLEANS0005 当一个可串列类型不选择 Orleans 源生成序列化。"
ms.date: 08/25/2026
ms.topic: reference
---

<a id="orleans0005-add-generateserializer"></a>

# ORLEANS0005:添加 GenerateSerializer

| 财产 | 价值值值 |
| --- | --- |
| 类别类 | 用法 |
| 严重性 | 信息信息 |
| 代码修正 | 可用 |

<a id="cause"></a>

## 原因原因原因

类型有 `[Serializable]` ,但没有 `[GenerateSerializer]`。

<a id="impact"></a>

## 影响影响

该类型不使用源生成的序列化 Orleans ,可错失 Orleans版本耐受性图和生成代码性能。

<a id="how-to-fix"></a>

## 如何修补

应用代码修正添加 `[GenerateSerializer]`,然后通过指定稳定成员身份或排除成员处理任何 `ORLEANS0004` 诊断。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

当 Orleans 从未连续排序或故意使用自定义或外部序列器时,禁止该类型。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0005.severity = none
```
