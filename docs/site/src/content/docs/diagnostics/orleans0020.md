---
title: "ORLEANS0020: 缺少 OrleansContracts.txt"
description: 了解并解决启用契约兼容性分析但缺少清单时出现的 ORLEANS0020。
ms.date: 08/27/2026
ms.topic: reference
---

<a id="orleans0020-orleanscontractstxt-is-missing"></a>

# ORLEANS0020: 缺少 OrleansContracts.txt

| 属性 | 值 |
| --- | --- |
| 类别 | 版本控制 |
| 严重性 | 信息 |
| 代码修复 | 可用 |

<a id="cause"></a>

## 原因

已启用契约兼容性分析，项目包含受跟踪的 Orleans 契约，但未找到配置的 `OrleansContracts.txt` 文件。

<a id="impact"></a>

## 影响

分析器没有基准，因此无法检测 RPC 标识、签名、版本或 Grain 类的变更。

<a id="how-to-fix"></a>

## 修复方法

应用 **Regenerate OrleansContracts.txt**，创建并填充完整项目清单。配置路径可以尚不存在；代码修复会创建该文件及其父目录。分别为每个契约项目执行重新生成，而不是传入解决方案路径。将生成的文件加入源代码管理，并使用[契约兼容性指南](../grains/grain-versioning/contract-compatibility-analyzer.md#regenerate-the-manifest)检查基准。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

如果项目不需要跟踪契约，请禁用分析器。否则抑制诊断会使项目主动启用分析器失去意义。

```xml
<EnableOrleansContractsAnalyzer>false</EnableOrleansContractsAnalyzer>
```

参阅[Orleans 契约兼容性分析器](../grains/grain-versioning/contract-compatibility-analyzer.md)。
