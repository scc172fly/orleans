---
title: "ORLEANS0012: 修改重复的序列化 Id"
description: 了解并解决序列化成员重复使用 Orleans 字段 ID 时出现的 ORLEANS0012。
ms.date: 08/25/2026
ms.topic: reference
---

<a id="orleans0012-change-a-duplicated-serialization-id"></a>

# ORLEANS0012: 修改重复的序列化 Id

| 属性 | 值 |
| --- | --- |
| 类别 | 用法 |
| 严重性 | 错误 |
| 代码修复 | 可用 |

<a id="cause"></a>

## 原因

同一个 `[GenerateSerializer]` 类型中有两个或更多成员使用相同的常量 `[Id]` 值。

<a id="impact"></a>

## 影响

字段 ID 歧义可能导致数据被反序列化到错误成员、损坏有效载荷，并破坏版本容错性。

<a id="how-to-fix"></a>

## 修复方法

恢复原有的字段 ID 映射。只为新增或标记错误的成员分配此前未使用的 ID，绝不要重新编号已部署的 ID。

代码修复会选择大于文档中最高 ID 的值。在应用 Fix All（全部修复）之前，请仔细检查结果。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

不要对由 Orleans 序列化的类型抑制此诊断。如果类型不属于 Orleans 序列化架构，请移除其序列化标注。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0012.severity = none
```
