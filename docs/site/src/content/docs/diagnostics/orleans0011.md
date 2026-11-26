---
title: "ORLEANS0011:重命名重复的别名"
description: "理解和决心 ORLEANS0011 何时 Orleans 化名碰撞。"
ms.date: 08/25/2026
ms.topic: reference
---

<a id="orleans0011-rename-a-duplicated-alias"></a>

# ORLEANS0011:重命名重复的别名

| 财产 | 价值值值 |
| --- | --- |
| 类别类 | 用法 |
| 严重性 | 错误错误 |
| 代码修正 | 部分可用 |

<a id="cause"></a>

## 原因原因原因

不同类型使用相同的非空 `[Alias]`,或在一个 grain 接口中使用相同别名的多种方法。

<a id="impact"></a>

## 影响影响

碰撞造成类型登记模糊、序列身份或辐射防护中心方法的发送。

<a id="how-to-fix"></a>

## 如何修补

给每个序列型号都提供一个全球独特的稳定别名,给每个 grain- 接口法都在其界面中提供一个独特的别名。

代码修正可以用数字后缀重命名后来重复的方法别名。 使用别名会要求刻意手动重命名 。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

禁止真正的碰撞。 只需在确认身份不能共存后, 仅制止经证明的假分析镜显示为阳性的。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0011.severity = none
```
