---
title: "ORLEANS0003: 继承 Grain（已移除）"
description: 了解旧版 Orleans 分析器包报告的、现已移除的 ORLEANS0003 诊断。
ms.date: 08/25/2026
ms.topic: reference
---

<a id="orleans0003-inherit-from-grain-removed"></a>

# ORLEANS0003: 继承 Grain（已移除）

| 属性 | 值 |
| --- | --- |
| 类别 | 用法 |
| 状态 | 已移除 |
| 代码修复 | 历史版本可用 |

<a id="cause"></a>

## 原因

旧版 Orleans 分析器包会在非抽象类实现 Grain 接口但未派生自 <xref:Orleans.Grain> 时报告此诊断。

<a id="impact"></a>

## 影响

当前 Orleans 版本通过 <xref:Orleans.IGrainBase> 支持 POCO Grain 类。此诊断已不再代表框架要求。

<a id="how-to-fix"></a>

## 修复方法

升级 Orleans 分析器包。POCO Grain 实现当前 Grain 激活契约后，便可以不继承 <xref:Orleans.Grain>。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

如果暂时无法升级，对明确受支持的 POCO Grain 抑制此已移除规则是安全的。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0003.severity = none
```
