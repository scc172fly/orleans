---
title: "ORLEANS0017: Grain 接口版本错配"
description: "理解和决心 ORLEANS0017 时 grain 介面版本不同 OrleansContracts.txt."
ms.date: 08/27/2026
ms.topic: reference
---

<a id="orleans0017-grain-interface-version-mismatch"></a>

# ORLEANS0017: Grain 接口版本错配

| 财产 | 价值值值 |
| --- | --- |
| 类别类 | 版本 |
| 严重性 | 警告 |
| 代码修正 | 可用 |

<a id="cause"></a>

## 原因原因原因

接口的 `[Version]` 值与 `OrleansContracts.txt`中记录的版本不同。没有 `[Version]` 的界面有版本 `0`。

<a id="impact"></a>

## 影响影响

单表不再描述运行时兼容性和路由决定所用的数字版本。 版本数的更改本身也并不使不兼容的方法或有效载荷的改变相互兼容。

<a id="how-to-fix"></a>

## 如何修补

确定源或显示的无意更改是否源或表单。 恢复上一个源版本, 或者审查滚动升级的影响并应用 **更新版本在 OrleansContracts.txt** 当新版本是有意的时。

应用 **Regenerate OrleansContracts.txt** 重建完整的项目清单。为每个合同项目分别运行再生,并利用 [合同兼容性指导](../grains/grain-versioning/contract-compatibility-analyzer.md#regenerate-the-manifest)审查产生的 diff 。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

仅在短时间的阶段编辑中使用抑制。 不要使用源和源代码显示版本错配释放 。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0017.severity = none
```

见 [部署新版本 grain](../grains/grain-versioning/deploying-new-versions-of-grains.md)。
