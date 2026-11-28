---
title: "ORLEANS0021: 重复的 Grain 接口声明"
description: 了解并解决 OrleansContracts.txt 多次声明同一接口标识时出现的 ORLEANS0021。
ms.date: 08/27/2026
ms.topic: reference
---

<a id="orleans0021-duplicate-grain-interface-declaration"></a>

# ORLEANS0021: 重复的 Grain 接口声明

| 属性 | 值 |
| --- | --- |
| 类别 | 版本控制 |
| 严重性 | 警告 |
| 代码修复 | 不可用 |

<a id="cause"></a>

## 原因

`OrleansContracts.txt` 中重复出现了有效的接口标识。如果存在 `GrainInterfaceType`，它就是有效标识；对于旧版声明，则按照 Orleans 约定从记录的 CLR 名称派生标识。

<a id="impact"></a>

## 影响

清单存在歧义。解析器保留第一个声明，因此兼容性审查可能使用错误的版本或方法集合。

<a id="how-to-fix"></a>

## 修复方法

将具有相同有效标识的声明合并为一个规范条目。标识迁移期间，如果活动声明和已停用声明记录了不同的显式 `GrainInterfaceType` 值，则可以共享同一 CLR 名称。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

请修复格式错误的清单，而不是抑制此诊断。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0021.severity = none
```
