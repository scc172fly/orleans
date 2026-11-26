---
title: "ORLEANS0003:从 Grain 继承遗产(已移走)"
description: "理解已删除的 ORLEANS0003 年龄较大的诊断报告 Orleans 分析包"
ms.date: 08/25/2026
ms.topic: reference
---

<a id="orleans0003-inherit-from-grain-removed"></a>

# ORLEANS0003:从 Grain 继承遗产(已移走)

| 财产 | 价值值值 |
| --- | --- |
| 类别类 | 用法 |
| 状况状况现状状况现况 | 已删除 |
| 代码修正 | 历史历史 |

<a id="cause"></a>

## 原因原因原因

年长 Orleans 分析器包在非抽取类实施 grain 界面,不来自 <xref:Orleans.Grain>.

<a id="impact"></a>

## 影响影响

现 Orleans 支持 POCO 版本 grain 课程通过 <xref:Orleans.IGrainBase>.这一诊断不再是一个框架要求。

<a id="how-to-fix"></a>

## 如何修补

升级 Orleans 分析器包件,一个POCO grain 保持独立 <xref:Orleans.Grain> 当它执行当前时 grain 启动合同

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

当升级暂时受阻时,对有意支持的POCO grain而言,取消这一被删除的规则是安全的。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0003.severity = none
```
