---
title: "ORLEANS0013: 从 Grain 类中移除序列化标识特性"
description: 了解并解决 Grain 实现被标记为进行数据序列化时出现的 ORLEANS0013。
ms.date: 08/25/2026
ms.topic: reference
---

<a id="orleans0013-remove-serialization-identity-attributes-from-a-grain-class"></a>

# ORLEANS0013: 从 Grain 类中移除序列化标识特性

| 属性 | 值 |
| --- | --- |
| 类别 | 用法 |
| 严重性 | 错误 |
| 代码修复 | 可用 |

<a id="cause"></a>

## 原因

派生自 <xref:Orleans.Grain> 的类（包括 `Grain<TState>`）标有 `[Alias]` 或 `[GenerateSerializer]`。

<a id="impact"></a>

## 影响

Grain 实现实例是激活对象，而不是应用程序有效载荷。序列化实现会引入不正确的数据契约，并可能造成别名冲突。

<a id="how-to-fix"></a>

## 修复方法

从 Grain 实现中移除 `[Alias]` 和 `[GenerateSerializer]`。改为在 Grain 状态和消息类型上应用序列化特性。

代码修复会移除包含无效特性的特性列表。当同一特性列表还包含无关特性时，请检查差异。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

仅在非常规集成明确要求独立于 Orleans 激活过程序列化该类时，才抑制此诊断。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0013.severity = none
```
