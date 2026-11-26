---
title: "ORLEANS0020: OrleansContracts.txt 丢失"
description: "当合同兼容性分析在没有显示器的情况下进行时,就能够理解和解决 ORLEANS0020 。"
ms.date: 08/27/2026
ms.topic: reference
---

<a id="orleans0020-orleanscontractstxt-is-missing"></a>

# ORLEANS0020: OrleansContracts.txt 丢失

| 财产 | 价值值值 |
| --- | --- |
| 类别类 | 版本 |
| 严重性 | 信息信息 |
| 代码修正 | 可用 |

<a id="cause"></a>

## 原因原因原因

合同兼容性分析得以进行,项目包含跟踪 Orleans 合同,未找到配置的 `OrleansContracts.txt` 档案。

<a id="impact"></a>

## 影响影响

分析器没有基线,因此无法检测到RPC的身份、签名、版本或 grain- 类变化。

<a id="how-to-fix"></a>

## 如何修补

应用 **Regenerate OrleansContracts.txt** 创建和输入完整的工程显示。 配置路径可能不存在; 代码修正创建文件及其父目录。 为每个合同项目单独运行再生, 而不是通过解决方案路径。 添加生成的文件以源控制并使用 [合同兼容性指导](../grains/grain-versioning/contract-compatibility-analyzer.md#regenerate-the-manifest)来审查基准 。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

如果项目不需要合同跟踪, 则禁用分析器。 否则抑制会挫败项目选择加入 。

```xml
<EnableOrleansContractsAnalyzer>false</EnableOrleansContractsAnalyzer>
```

见 [Orleans 合同兼容性分析器](../grains/grain-versioning/contract-compatibility-analyzer.md)。
