---
title: Grain 版本选择器策略
description: 在 Orleans 放置新激活实例时选择符合条件的 Grain 接口版本。
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="grain-version-selector-strategies"></a>

# Grain 版本选择器策略

在兼容性策略筛选可用接口版本后，选择器策略决定哪些版本仍符合放置条件。

| 策略 | 符合条件的版本 |
|---|---|
| <xref:Orleans.Versions.Selector.AllCompatibleVersions> | 所有兼容版本 |
| <xref:Orleans.Versions.Selector.LatestVersion> | 最高的兼容版本 |
| <xref:Orleans.Versions.Selector.MinimumVersion> | 最低的兼容版本 |

<a id="all-compatible-versions"></a>

## 所有兼容版本

<xref:Orleans.Versions.Selector.AllCompatibleVersions> 为默认策略。它返回所有兼容版本，Orleans 放置机制会选择兼容的 Silo。因此，分布取决于可用的兼容 Silo，而非保证每个版本获得相同的百分比。

当请求版本为 1、可用版本为 1 和 2，且使用 <xref:Orleans.Versions.Compatibility.BackwardCompatible> 时，两个版本均符合条件。

<a id="latest-version"></a>

## 最新版本

<xref:Orleans.Versions.Selector.LatestVersion> 仅返回最高的兼容版本。它在保留兼容性筛选的同时，使新激活实例趋向最新部署的版本。

当请求版本为 1、可用版本为 1 和 2，且使用 <xref:Orleans.Versions.Compatibility.BackwardCompatible> 时，仅版本 2 符合条件。若请求版本为 3，则两个版本均不兼容，放置机制无法满足该请求。

<a id="minimum-version"></a>

## 最低版本

<xref:Orleans.Versions.Selector.MinimumVersion> 仅返回最低的兼容版本。在分阶段验证期间，它可以让较旧的兼容实现继续服务旧调用方。

使用 <xref:Orleans.Versions.Compatibility.BackwardCompatible> 时，若请求版本为 1、可用版本为 2 和 3，则选中版本 2。若请求版本为 3，则仅版本 3 或更高版本可能兼容；选择器绝不会绕过兼容性规则选择版本 2。

<a id="existing-activations"></a>

## 现有激活实例

更改选择器会影响新激活实例的放置，但不会主动替换兼容的现有激活实例。当某激活实例与请求不兼容、正常停用或其所在 Silo 离开集群时，该激活实例才会被替换。
