---
title: "ORLEANS0025:重复 grain 类声明"
description: "理解和决心 ORLEANS0025 何时 OrleansContracts.txt 宣布a grain 身份不止一次"
ms.date: 08/27/2026
ms.topic: reference
---

<a id="orleans0025-duplicate-grain-class-declaration"></a>

# ORLEANS0025:重复 grain 类声明

| 财产 | 价值值值 |
| --- | --- |
| 类别类 | 版本 |
| 严重性 | 警告 |
| 代码修正 | 不详 |

<a id="cause"></a>

## 原因原因原因

`OrleansContracts.txt` 重复有效的 grain 类身份。有效身份是存在时的 `GrainType` ,以及根据记录中的CLR名称得出的身份,使用 Orleans 公约来进行遗留声明。

<a id="impact"></a>

## 影响影响

grain 身份历史变得模糊不清,分析者只接受第一份声明。

<a id="how-to-fix"></a>

## 如何修补

将具有相同有效身份的声明合并为一个卡尼基门入境。 当声明在身份移徙中记录不同的明确 `GrainType` 值时, 主动和退休声明可以共享一个 CLR 名称 。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

修复错误的显示器,而不是抑制诊断。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0025.severity = none
```
