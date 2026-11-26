---
title: "ORLEANS0022: Grain 类在 OrleansContracts.txt中不活跃"
description: "理解和决心 ORLEANS0022 当混凝土 grain 合同清单中缺少或退职的等级。"
ms.date: 08/27/2026
ms.topic: reference
---

<a id="orleans0022-grain-class-is-not-active-in-orleanscontractstxt"></a>

# ORLEANS0022: Grain 类在 OrleansContracts.txt中不活跃

| 财产 | 价值值值 |
| --- | --- |
| 类别类 | 版本 |
| 严重性 | 警告 |
| 代码修正 | 可用 |

<a id="cause"></a>

## 原因原因原因

混凝土 grain 或系统目标类没有与其有效 `GrainType`相符的主动明确声明。

<a id="impact"></a>

## 影响影响

实施身份不受合同审查的保护。没有稳定的 `[GrainType]`或 `GrainType` 更改的 CLR 重命名,可创建一种独特的逻辑 grain 类型,并干扰路线或状态连续性。

<a id="how-to-fix"></a>

## 如何修补

验证该类的耐久性 grain 类型, 当必须保持独立于 CLR 名称时添加 `[GrainType]` , 并应用 **添加到 OrleansContracts.txt**。代码修正会增加或重新启动分类声明 。

应用 **Regenerate OrleansContracts.txt** 重建完整的项目清单。为每个合同项目分别运行再生,并利用 [合同兼容性指导](../grains/grain-versioning/contract-compatibility-analyzer.md#regenerate-the-manifest)审查产生的 diff 。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

仅对故意被排除在部署合同跟踪之外的 grain 类禁止。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0022.severity = none
```
