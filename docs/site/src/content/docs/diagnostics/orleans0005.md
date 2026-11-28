---
title: "ORLEANS0005: 添加 GenerateSerializer"
description: 了解并解决 Serializable 类型未启用 Orleans 源生成序列化时出现的 ORLEANS0005。
ms.date: 08/25/2026
ms.topic: reference
---

<a id="orleans0005-add-generateserializer"></a>

# ORLEANS0005: 添加 GenerateSerializer

| 属性 | 值 |
| --- | --- |
| 类别 | 用法 |
| 严重性 | 信息 |
| 代码修复 | 可用 |

<a id="cause"></a>

## 原因

类型具有 `[Serializable]`，但没有 `[GenerateSerializer]`。

<a id="impact"></a>

## 影响

该类型不使用 Orleans 源生成序列化，因此可能无法利用 Orleans 的版本容错架构和生成代码的性能优势。

<a id="how-to-fix"></a>

## 修复方法

应用代码修复以添加 `[GenerateSerializer]`，然后为成员分配稳定 ID 或排除成员，以解决所有 `ORLEANS0004` 诊断。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

当类型从不由 Orleans 序列化，或明确使用自定义或外部序列化器时，可以抑制此诊断。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0005.severity = none
```
