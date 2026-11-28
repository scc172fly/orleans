---
title: "ORLEANS0023: Grain 类标识不匹配"
description: 了解并解决 Grain 类的 GrainType 与 OrleansContracts.txt 不同时出现的 ORLEANS0023。
ms.date: 08/27/2026
ms.topic: reference
---

<a id="orleans0023-grain-class-identity-mismatch"></a>

# ORLEANS0023: Grain 类标识不匹配

| 属性 | 值 |
| --- | --- |
| 类别 | 版本控制 |
| 严重性 | 警告 |
| 代码修复 | 可用 |

<a id="cause"></a>

## 原因

Grain 类按 CLR 名称匹配了清单中的声明，但源码中的有效 `GrainType` 与清单不同。

<a id="impact"></a>

## 影响

这是运行时标识变更。它可能创建新的逻辑 Grain 类型，改变路由及存储标识，并破坏混合版本运行。

<a id="how-to-fix"></a>

## 修复方法

如果是意外变更，请恢复先前的 `[GrainType]`。仅当标识的不兼容变更是有意的，并且已经设计其部署和状态迁移方案时，才更新清单。

**Update grain class alias in OrleansContracts.txt** 代码修复会将源码标识接受为新的基准。应用之前请检查标识变更。

应用 **Regenerate OrleansContracts.txt** 以重建完整项目清单。分别为各契约项目执行重新生成，并依据[契约兼容性指南](../grains/grain-versioning/contract-compatibility-analyzer.md#regenerate-the-manifest)检查生成的差异。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

仅对有意实施且已有文档记录的标识迁移抑制诊断。优先更新经过审查的基准，而不是保留抑制。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0023.severity = none
```
