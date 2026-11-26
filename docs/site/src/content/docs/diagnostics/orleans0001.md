---
title: "ORLEANS0001:将 AlwaysInterleave 放在 grain 接口上"
description: "理解和决心 ORLEANS0001 何时 AlwaysInterleave 适用于执行方法。"
ms.date: 08/25/2026
ms.topic: reference
---

<a id="orleans0001-place-alwaysinterleave-on-the-grain-interface"></a>

# ORLEANS0001:将 AlwaysInterleave 放在 grain 接口上

| 财产 | 价值值值 |
| --- | --- |
| 类别类 | 用法 |
| 严重性 | 错误错误 |
| 代码修正 | 不详 |

<a id="cause"></a>

## 原因原因原因

`[AlwaysInterleave]` 适用于一个类别所申报的方法。属性定义了 grain 合同中的排期行为,属于相应的 grain- 界面方法。

<a id="impact"></a>

## 影响影响

执行注释并没有定义远程合同的时间安排行为。 电话不会像预期的那样中断,这可能造成生命或同货币问题。

<a id="how-to-fix"></a>

## 如何修补

删除执行方法中的 `[AlwaysInterleave]` ,并将其应用到 grain 接口上的匹配方法。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

仅对验证的检测器在不是 grain 执行的一部分的方法上虚正的反向分析器进行禁止。 最好删除无效属性 。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0001.severity = none
```
