---
title: "ORLEANS0022: Grain 类在 OrleansContracts.txt 中未处于活动状态"
description: 了解并解决契约清单中缺少具体 Grain 类或该类已停用时出现的 ORLEANS0022。
ms.date: 08/27/2026
ms.topic: reference
---

<a id="orleans0022-grain-class-is-not-active-in-orleanscontractstxt"></a>

# ORLEANS0022: Grain 类在 OrleansContracts.txt 中未处于活动状态

| 属性 | 值 |
| --- | --- |
| 类别 | 版本控制 |
| 严重性 | 警告 |
| 代码修复 | 可用 |

<a id="cause"></a>

## 原因

具体 Grain 类或系统目标类在清单中没有与其有效 `GrainType` 匹配的活动声明。

<a id="impact"></a>

## 影响

实现标识未受到契约审查保护。没有稳定 `[GrainType]` 的 CLR 重命名，或 `GrainType` 变更，都可能创建不同的逻辑 Grain 类型，并破坏路由或状态连续性。

<a id="how-to-fix"></a>

## 修复方法

核实类的持久标识所对应的 Grain 类型；需要独立于 CLR 名称保持稳定时添加 `[GrainType]`，然后应用 **Add to OrleansContracts.txt**。代码修复会添加或重新激活类声明。

应用 **Regenerate OrleansContracts.txt** 以重建完整项目清单。分别为各契约项目执行重新生成，并依据[契约兼容性指南](../grains/grain-versioning/contract-compatibility-analyzer.md#regenerate-the-manifest)检查生成的差异。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

仅对明确排除在部署契约跟踪范围之外的 Grain 类抑制诊断。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0022.severity = none
```
