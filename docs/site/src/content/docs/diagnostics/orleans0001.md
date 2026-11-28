---
title: "ORLEANS0001: 将 AlwaysInterleave 放在 Grain 接口上"
description: 了解并解决将 AlwaysInterleave 应用于实现方法时出现的 ORLEANS0001。
ms.date: 08/25/2026
ms.topic: reference
---

<a id="orleans0001-place-alwaysinterleave-on-the-grain-interface"></a>

# ORLEANS0001: 将 AlwaysInterleave 放在 Grain 接口上

| 属性 | 值 |
| --- | --- |
| 类别 | 用法 |
| 严重性 | 错误 |
| 代码修复 | 不可用 |

<a id="cause"></a>

## 原因

`[AlwaysInterleave]` 应用于类中声明的方法。此特性定义 Grain 契约上的调度行为，应放在相应的 Grain 接口方法上。

<a id="impact"></a>

## 影响

实现上的特性标注不会定义远程契约的调度行为。调用可能无法按预期交错执行，从而引发活性或并发问题。

<a id="how-to-fix"></a>

## 修复方法

从实现方法中移除 `[AlwaysInterleave]`，并将它应用于 Grain 接口上的对应方法。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

仅在已确认分析器对不属于 Grain 实现的方法产生误报时，才抑制此诊断。优先移除无效的特性。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0001.severity = none
```
