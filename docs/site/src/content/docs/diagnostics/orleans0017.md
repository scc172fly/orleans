---
title: "ORLEANS0017: Grain 接口版本不匹配"
description: 了解并解决 Grain 接口版本与 OrleansContracts.txt 不同时出现的 ORLEANS0017。
ms.date: 08/27/2026
ms.topic: reference
---

<a id="orleans0017-grain-interface-version-mismatch"></a>

# ORLEANS0017: Grain 接口版本不匹配

| 属性 | 值 |
| --- | --- |
| 类别 | 版本控制 |
| 严重性 | 警告 |
| 代码修复 | 可用 |

<a id="cause"></a>

## 原因

接口的 `[Version]` 值与 `OrleansContracts.txt` 中记录的版本不同。没有 `[Version]` 的接口版本为 `0`。

<a id="impact"></a>

## 影响

清单不再描述运行时兼容性和路由决策所使用的数字版本。仅更改版本号本身也不能使不兼容的方法或有效载荷变更变得兼容。

<a id="how-to-fix"></a>

## 修复方法

确定源码或清单是否发生了意外变更。恢复先前的源码版本；如果新版本是有意变更的，则检查其对滚动升级的影响，并应用 **Update version in OrleansContracts.txt**。

应用 **Regenerate OrleansContracts.txt** 以重建完整项目清单。分别为各契约项目执行重新生成，并依据[契约兼容性指南](../grains/grain-versioning/contract-compatibility-analyzer.md#regenerate-the-manifest)检查生成的差异。

<a id="suppress-the-diagnostic"></a>

## 抑制诊断

仅在短期分阶段编辑期间使用抑制。不要在源码版本与清单版本不匹配时发布。

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0017.severity = none
```

参阅[部署新的 Grain 版本](../grains/grain-versioning/deploying-new-versions-of-grains.md)。
