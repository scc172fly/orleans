---
title: 滚动部署中的版本差异
description: 解释在 Silo 运行不同构建版本时，Orleans 如何路由 Grain 接口版本并保持兼容性。
ms.date: 08/11/2026
ms.topic: concept-article
---

<a id="rolling-version-skew"></a>

# 滚动部署中的版本差异

滚动部署期间，Silo 可能公布同一 Grain 接口的不同版本。Orleans 区分两个问题：**兼容性**判断候选版本是否可以为请求版本提供服务，而**选择**决定使用哪个兼容候选版本。Grain 版本清单、兼容性决策器、选择器和放置服务共同回答这些问题。

<a id="version-discovery-and-selection"></a>

## 版本发现与选择

每个 Silo 向 `GrainVersionManifest` 提供其支持的接口版本。带缓存的选择器管理器按 Grain 类型、接口和请求版本为结果建立键。它获取可用版本，通过配置的兼容性决策器进行筛选，选择一个或多个版本，并将这些版本映射到合适的 Silo。版本或兼容性策略变化时缓存失效。

内置策略有意采用不同的升级行为：

| 策略 | 效果 |
| --- | --- |
| 严格兼容 | 仅同一接口版本符合条件。 |
| 向后兼容 | 当决策器判定兼容时，较新实现可以服务较旧请求。 |
| 所有版本兼容 | 所有公布的版本均符合条件。 |
| 最新版本选择器 | 选择最高的兼容版本。 |
| 最低版本选择器 | 选择最低的兼容版本。 |
| 所有兼容版本选择器 | 保留所有兼容版本作为放置候选。 |

选择器将兼容性决策转换为放置候选集合；应用需在该集合内保持契约兼容性。

<a id="activation-checks"></a>

## 激活实例检查

放置选择合适的 Silo，激活实例则根据本地实现验证传入的接口版本。不兼容的激活实例使陈旧路由失效，并将现有消息重新交给路由流程。因此版本变化可能移动激活实例或重复路由，同时保持逻辑调用不变。

<a id="wire-compatibility-is-a-separate-contract"></a>

## 传输格式兼容性是独立契约

只要混合版本流量、排队消息、提醒器、流或持久化状态可能跨越版本边界，版本路由就要求请求及响应类型可被新旧构建版本读取。保持序列化 ID 稳定，使用新 ID 添加字段，在 CLR 名称变化时保留别名，并确保自定义编解码器跳过未知字段。传输格式层面的规则参见[序列化与代码生成内部机制](serialization.md)。

Grain 状态兼容性仍由应用负责其数据架构。只要仍可能回滚或进行混合版本放置，两种实现都要能够读取存储表示形式，并容忍对方写入的数据。

<a id="failure-modes-and-rollout-implications"></a>

## 故障模式与发布影响

- 兼容 Silo 集合为空会导致放置拒绝请求或请求超时。
- 陈旧激活实例触发同一逻辑消息的缓存失效及路由修复；运行时在定位兼容激活实例时保留消息标识。
- 更改选择器或兼容性策略会重置合适 Silo 的缓存，使后续放置反映新策略。
- 在所有调用方及持久化数据均兼容之前移除旧实现，可能使计划中的滚动部署变成服务中断。

[接口版本控制指南](../grains/grain-versioning/grain-versioning.md) 及[部署与回滚指南](../migration/deployment-and-rollback.md) 介绍配置及发布过程。运行时决策链将接口清单、兼容性选择、放置、激活实例验证及传输格式兼容性联系起来。

源码：[`GrainVersionManifest`](https://github.com/dotnet/orleans/blob/main/src/Orleans.Core/Manifest/GrainVersionManifest.cs)、[`CachedVersionSelectorManager`](https://github.com/dotnet/orleans/blob/main/src/Orleans.Runtime/Versions/CachedVersionSelectorManager.cs)、[`AllCompatibleVersionsSelector`](https://github.com/dotnet/orleans/blob/main/src/Orleans.Runtime/Versions/Selector/AllCompatibleVersionsSelector.cs) 和 [`ActivationData`](https://github.com/dotnet/orleans/blob/main/src/Orleans.Runtime/Catalog/ActivationData.cs)。
