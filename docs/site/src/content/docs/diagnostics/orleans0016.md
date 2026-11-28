---
title: "ORLEANS0016: Grain 接口在 OrleansContracts.txt 中未处于活动状态"
description: 了解并解决契约清单中缺少 Grain 接口或接口已停用时出现的 ORLEANS0016。
ms.date: 08/27/2026
ms.topic: reference
---

<a id="orleans0016-grain-interface-is-not-active-in-orleanscontractstxt"></a>

# ORLEANS0016: Grain 接口在 OrleansContracts.txt 中未处于活动状态

| 属性 | 值 |
| --- | --- |
| 类别 | 版本控制 |
| 严重性 | 警告 |
| 代码修复 | 可用 |

<a id="cause"></a>

## 原因

契约分析器发现一个 Grain 接口，其 `GrainInterfaceType` 在 `OrleansContracts.txt` 中没有匹配的活动声明。匹配的声明也可能存在，但已标为 `*RETIRED*`。

<a id="impact"></a>

## 影响

接口标识、版本及方法未纳入契约审查。重命名或标识变更可能在清单没有显式差异的情况下成为新的 RPC 契约。

<a id="how-to-fix"></a>

## 修复方法

核实接口标识及版本，再应用 **Add to OrleansContracts.txt**。代码修复会添加或重新激活接口，并记录其普通实例方法。

如果该接口是意外恢复的，请从源码中移除它，或引入另行命名的替代接口，而不是复用已停用的标识。

应用 **Regenerate OrleansContracts.txt** 以重建完整项目清单。分别为各契约项目执行重新生成，并依据[契约兼容性指南](../grains/grain-versioning/contract-compatibility-analyzer.md#regenerate-the-manifest)检查生成的差异。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

可部署的 RPC 契约应保留在清单中。如果项目明确不维护契约清单，请为项目禁用契约分析器，而不是抑制各个接口的诊断。

```xml
<EnableOrleansContractsAnalyzer>false</EnableOrleansContractsAnalyzer>
```

参阅[Orleans 契约兼容性分析器](../grains/grain-versioning/contract-compatibility-analyzer.md)。
