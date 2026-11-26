---
title: "ORLEANS0021:重复 grain 接口声明"
description: "理解和决心 ORLEANS0021 何时 OrleansContracts.txt 多次声明接口身份 。"
ms.date: 08/27/2026
ms.topic: reference
---

<a id="orleans0021-duplicate-grain-interface-declaration"></a>

# ORLEANS0021:重复 grain 接口声明

| 财产 | 价值值值 |
| --- | --- |
| 类别类 | 版本 |
| 严重性 | 警告 |
| 代码修正 | 不详 |

<a id="cause"></a>

## 原因原因原因

`OrleansContracts.txt` 重复有效的接口身份。有效身份是 `GrainInterfaceType` ,在存在时和从所记录的CLR名称中得出的身份,使用 Orleans 公约作为遗留声明的保存。

<a id="impact"></a>

## 影响影响

清单含糊不清。 分析师保留了第一项声明, 所以兼容性审查可以使用错误的版本或方法集 。

<a id="how-to-fix"></a>

## 如何修补

将具有相同有效身份的声明合并为一个卡尼基门入境。 当声明在身份移徙中记录不同的明确 `GrainInterfaceType` 值时, 主动和退休声明可以共享一个 CLR 名称 。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

修复错误的显示器,而不是抑制诊断。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0021.severity = none
```
