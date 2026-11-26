---
title: "ORLEANS0008: Grain 接口无法包含属性"
description: "理解和决心 ORLEANS0008 时 Orleans 远程界面声明实例属性 。"
ms.date: 08/25/2026
ms.topic: reference
---

<a id="orleans0008-grain-interfaces-cannot-contain-properties"></a>

# ORLEANS0008: Grain 接口无法包含属性

| 财产 | 价值值值 |
| --- | --- |
| 类别类 | 用法 |
| 严重性 | 错误错误 |
| 代码修正 | 不详 |

<a id="cause"></a>

## 原因原因原因

用于 Orleans 远程合同的界面声明实例属性。

<a id="impact"></a>

## 影响影响

属性不定义支持的直线 Orleans RPC 操作,并模糊非同步远程呼叫边界。

<a id="how-to-fix"></a>

## 如何修补

将属性替换为使用支持的 grain-调用返回类型时的获取或设定方法。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

只有当接口继承 Orleans 可地址时, 才能禁止使用该界面, 但事实证明它永远无法被远程覆盖 。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0008.severity = none
```
