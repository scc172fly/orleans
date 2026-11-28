---
title: "ORLEANS0008: Grain 接口不能包含属性"
description: 了解并解决 Orleans 远程接口声明实例属性时出现的 ORLEANS0008。
ms.date: 08/25/2026
ms.topic: reference
---

<a id="orleans0008-grain-interfaces-cannot-contain-properties"></a>

# ORLEANS0008: Grain 接口不能包含属性

| 属性 | 值 |
| --- | --- |
| 类别 | 用法 |
| 严重性 | 错误 |
| 代码修复 | 不可用 |

<a id="cause"></a>

## 原因

用作 Orleans 远程契约的接口声明了实例属性。

<a id="impact"></a>

## 影响

属性不能定义受支持的显式 Orleans RPC 操作，并会掩盖异步远程调用的边界。

<a id="how-to-fix"></a>

## 修复方法

将属性替换为使用受支持 Grain 调用返回类型的异步获取或设置方法。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

仅当接口继承了 Orleans 可寻址性契约，但已证实从不会用于远程调用时抑制诊断。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0008.severity = none
```
