---
title: "ORLEANS0006: 静态或抽象成员不能序列化"
description: 了解并解决将 Orleans 序列化 ID 分配给无效成员时出现的 ORLEANS0006。
ms.date: 08/25/2026
ms.topic: reference
---

<a id="orleans0006-static-or-abstract-members-cannot-be-serialized"></a>

# ORLEANS0006: 静态或抽象成员不能序列化

| 属性 | 值 |
| --- | --- |
| 类别 | 用法 |
| 严重性 | 错误 |
| 代码修复 | 不可用 |

<a id="cause"></a>

## 原因

`[GenerateSerializer]` 类型中的静态或抽象成员被标记了 `[Id]`。

<a id="impact"></a>

## 影响

该成员没有 Orleans 可以序列化的具体实例级存储，因此声明的架构无效。

<a id="how-to-fix"></a>

## 修复方法

从静态或抽象成员中移除 `[Id]`。改为序列化具体实例成员；如果抽象属性表示需要序列化的状态，则将它改为具体属性。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

仅在已确认分析器或编译器存在不一致时抑制此诊断。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0006.severity = none
```
