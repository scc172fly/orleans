---
title: "ORLEANS0023: Grain 类身份错配"
description: "理解和决心 ORLEANS0023 时 grain 类 GrainType 与 OrleansContracts.txt."
ms.date: 08/27/2026
ms.topic: reference
---

<a id="orleans0023-grain-class-identity-mismatch"></a>

# ORLEANS0023: Grain 类身份错配

| 财产 | 价值值值 |
| --- | --- |
| 类别类 | 版本 |
| 严重性 | 警告 |
| 代码修正 | 可用 |

<a id="cause"></a>

## 原因原因原因

grain 类符合以氯乙烯单体名称表示的申报单,但其有效来源 `GrainType` 与清单不同。

<a id="impact"></a>

## 影响影响

这是一个运行时身份更改。 它可以创建新的逻辑 grain 类型, 改变路径和存储身份, 并打破混合转换操作 。

<a id="how-to-fix"></a>

## 如何修补

当更改是意外时恢复上一个 `[GrainType]` 。只有在身份中断是故意的,且其部署和状态迁移设计已经设计好时,才更新该表 。

**更新 grain 类别名,在 OrleansContracts.txt** 代码修正中接受源身份为新基准。在应用前先审查身份变化情况。

应用 **Regenerate OrleansContracts.txt** 重建完整的项目清单。为每个合同项目分别运行再生,并利用 [合同兼容性指导](../grains/grain-versioning/contract-compatibility-analyzer.md#regenerate-the-manifest)审查产生的 diff 。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

仅对蓄意、有文件证明的身份移徙进行禁止,更新审查的基准比保留禁止要好。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0023.severity = none
```
