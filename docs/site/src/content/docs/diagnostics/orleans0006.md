---
title: "ORLEANS0006:固定或抽象成员不能序列化"
description: "理解和决心 ORLEANS0006 当一个无效成员被指派 Orleans 序列号"
ms.date: 08/25/2026
ms.topic: reference
---

<a id="orleans0006-static-or-abstract-members-cannot-be-serialized"></a>

# ORLEANS0006:固定或抽象成员不能序列化

| 财产 | 价值值值 |
| --- | --- |
| 类别类 | 用法 |
| 严重性 | 错误错误 |
| 代码修正 | 不详 |

<a id="cause"></a>

## 原因原因原因

`[GenerateSerializer]` 类型中的静态或抽象成员标记为 `[Id]`。

<a id="impact"></a>

## 影响影响

成员没有具体的每个单位储存, Orleans 可以序列化,因此宣布的系统图无效。

<a id="how-to-fix"></a>

## 如何修补

从静态或抽象成员中删除 `[Id]` 。 将具体实例成员排序为序列化成员,或者在代表序列化状态时将抽象属性变为混凝土。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

仅对经校验的分析器或编译器不匹配进行禁止 。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0006.severity = none
```
