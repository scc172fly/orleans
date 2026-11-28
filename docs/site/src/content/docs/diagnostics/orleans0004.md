---
title: "ORLEANS0004: 添加缺失的序列化特性"
description: 了解并解决使用生成式序列化器的类型中存在未标注成员时出现的 ORLEANS0004。
ms.date: 08/25/2026
ms.topic: reference
---

<a id="orleans0004-add-missing-serialization-attributes"></a>

# ORLEANS0004: 添加缺失的序列化特性

| 属性 | 值 |
| --- | --- |
| 类别 | 用法 |
| 严重性 | 错误 |
| 代码修复 | 可用 |

<a id="cause"></a>

## 原因

标有 `[GenerateSerializer]` 的类、结构体或记录类型含有未标注 `[Id]` 或 `[NonSerialized]` 的实例字段或自动属性。

<a id="impact"></a>

## 影响

未标注的成员可能被 Orleans 序列化过程省略，导致值为默认值或丢失，以及版本容错架构不完整。

<a id="how-to-fix"></a>

## 修复方法

为需要序列化的成员分配稳定且唯一的 `[Id(n)]` 值。使用 `[NonSerialized]` 标记有意排除的成员。

代码修复可以从现有最大 ID 之后依次添加 ID，也可以将所有未标注的成员标记为不序列化。提交之前请检查生成的 ID，绝不要重新编号已经部署的字段。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

对于有意排除的成员，优先使用 `[NonSerialized]`。仅在测试夹具或旧版类型明确要求省略这些成员时抑制诊断。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0004.severity = none
```
