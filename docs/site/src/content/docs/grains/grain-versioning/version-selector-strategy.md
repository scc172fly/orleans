---
title: "Grain 版本选择器战略"
description: "选择符合条件 grain 用户 Orleans 设置新的激活。"
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="grain-version-selector-strategies"></a>

# Grain 版本选择器战略

在兼容战略过滤现有接口版本后,选择器战略确定哪些版本仍有资格被安插。

| 战略战略战略战略 战略 | 合格版本 |
|---|---|
| <xref:Orleans.Versions.Selector.AllCompatibleVersions> | 每个兼容版本 |
| <xref:Orleans.Versions.Selector.LatestVersion> | 最高兼容版本 |
| <xref:Orleans.Versions.Selector.MinimumVersion> | 最低兼容版本 |

<a id="all-compatible-versions"></a>

## 所有兼容版本

<xref:Orleans.Versions.Selector.AllCompatibleVersions> is the default. It returns all compatible versions, and Orleans placement selects a compatible silo. Distribution therefore follows the available compatible silos, not a guaranteed equal percentage per version.

请求版本1、现有版本1和2以及 <xref:Orleans.Versions.Compatibility.BackwardCompatible>,两个版本都符合资格。

<a id="latest-version"></a>

## 最近版本

<xref:Orleans.Versions.Selector.LatestVersion> returns only the highest compatible version. It moves new activations toward the newest deployment while preserving the compatibility filter.

请求版本1、现有版本1和2以及 <xref:Orleans.Versions.Compatibility.BackwardCompatible>只有第2版符合资格,请求版本3不兼容,因此位置安排无法满足请求。

<a id="minimum-version"></a>

## 最低版本

<xref:Orleans.Versions.Selector.MinimumVersion> returns only the lowest compatible version. It can keep older compatible implementations serving older callers during staged validation.

请求版本1和在 <xref:Orleans.Versions.Compatibility.BackwardCompatible>下现有版本2和3,选择了第2版。请求版本3只有第3版或新版本可以兼容;选择者从未绕过选择版本2的兼容性规则。

<a id="existing-activations"></a>

## 现有启动

更改选择器会影响新激活的放置。 它不会主动替换兼容的现有激活。 当激活与请求不符时, 通常停止使用, 或者其 silo 离开集束时会被替换 。
