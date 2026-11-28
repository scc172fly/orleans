---
title: "ORLEANS0025: 重复的 Grain 类声明"
description: 了解并解决 OrleansContracts.txt 多次声明同一 Grain 标识时出现的 ORLEANS0025。
ms.date: 08/27/2026
ms.topic: reference
---

<a id="orleans0025-duplicate-grain-class-declaration"></a>

# ORLEANS0025: 重复的 Grain 类声明

| 属性 | 值 |
| --- | --- |
| 类别 | 版本控制 |
| 严重性 | 警告 |
| 代码修复 | 不可用 |

<a id="cause"></a>

## 原因

`OrleansContracts.txt` 中重复出现了有效的 Grain 类标识。如果存在 `GrainType`，它就是有效标识；对于旧版声明，则按照 Orleans 约定从记录的 CLR 名称派生标识。

<a id="impact"></a>

## 影响

Grain 标识历史产生歧义，解析器只接受第一个声明。

<a id="how-to-fix"></a>

## 修复方法

将具有相同有效标识的声明合并为一个规范条目。标识迁移期间，如果活动声明和已停用声明记录了不同的显式 `GrainType` 值，则可以共享同一 CLR 名称。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

请修复格式错误的清单，而不是抑制此诊断。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0025.severity = none
```
