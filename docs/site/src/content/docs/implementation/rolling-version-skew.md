---
title: "滚动版本折叠"
description: "解释 Orleans 路径 grain 接口版本如何在 silos 运行不同的建筑时保持兼容性。"
ms.date: 08/11/2026
ms.topic: concept-article
---

<a id="rolling-version-skew"></a>

# 滚动版本折叠

在滚动部署期间, silos 可公布同一 grain 接口的不同版本。 Orleans 将两个问题分开: **兼容性** 询问所请求的版本能否由一个候选版本服务, **选择** 选择适合使用的候选人。 grain 版本显示、兼容性主任、甄选人和职位安排服务将对这些问题一并回答。

<a id="version-discovery-and-selection"></a>

## 发现和选择版本

每个 silo 都向 `GrainVersionManifest`贡献支持的接口版本。缓存选择器管理器以 grain 类型、界面和请求版本键入结果。它获取可用版本,通过配置兼容性主任过滤这些版本,选择一个或多个版本,并将这些版本映射为适合的 silos。当版本或兼容性战略发生改变时,缓存无效。

内置策略有意地有不同的升级行为:

| 战略战略战略战略 战略 | 效果效果 |
| --- | --- |
| 严格兼容性 | 只有相同的界面版本才有资格使用。 |
| 后向兼容 | 新的实施程序如果主任说可以兼容,则可能满足旧的要求。 |
| 所有版本兼容 | 每个广告版都符合资格。 |
| 最新选择器 | 选择最高兼容版本 。 |
| 最小最小选择器 | 选择最低兼容版本 。 |
| 所有兼容的选择器 | 将所有兼容版本都保留为职位安置候选人。 |

选择器将兼容性决定转换成一个放置装置;应用程序保持整个成套装置的合同兼容性。

<a id="activation-checks"></a>

## 启动检查

放置选择合适的 silo, 激活将对照本地执行来验证输入的接口版本。 不兼容的激活会使已停止的路径失效, 并将现有信件返回路由。 因此版本更改可以移动激活或重复路由, 同时保留逻辑调用 。

<a id="wire-compatibility-is-a-separate-contract"></a>

## 电线兼容性是单独合同

版本路径需要由新旧建筑和新建筑来读取的请求和回复类型,只要混合交通、排队信件、提醒、流或持续状态能够跨越边界。保持序列化标识稳定,添加新的身份识别字段,在移动 CLR 名称时保留别名,并确保自定义编码器跳过未知字段。 线级规则见 [内部序列和代码生成](serialization.md) 。

Grain- 状态兼容性仍然是应用计划的责任。执行者读取存储的表示,从另一个处听信写作,但退步或混合安置仍有可能。

<a id="failure-modes-and-rollout-implications"></a>

## 失效模式和推出所涉影响

- 空兼容 -silo 设置了导致拒绝或超时请求的位置。
- 停止激活会触发缓存失效,并修复同一逻辑电文的路径;运行时间在定位兼容的激活时保留其信息身份。
- 更改选择器或兼容性战略,将合适的silo 缓存重新设置,因此随后的放置反映了新政策。
- 在所有电话和耐用数据兼容之前取消旧的安装,可以将计划进行的滚动部署变成停机。

[界面版本指南](../grains/grain-versioning/grain-versioning.md) 和 [部署和回滚指导](../migration/deployment-and-rollback.md) 涵盖配置和推出程序。运行时的决定链连接界面表、兼容性选择、位置安排、激活验证和线条兼容性。

来源: [`GrainVersionManifest`](https://github.com/dotnet/orleans/blob/main/src/Orleans.Core/Manifest/GrainVersionManifest.cs)、 [`CachedVersionSelectorManager`](https://github.com/dotnet/orleans/blob/main/src/Orleans.Runtime/Versions/CachedVersionSelectorManager.cs)、 [`AllCompatibleVersionsSelector`](https://github.com/dotnet/orleans/blob/main/src/Orleans.Runtime/Versions/Selector/AllCompatibleVersionsSelector.cs)和 [`ActivationData`](https://github.com/dotnet/orleans/blob/main/src/Orleans.Runtime/Catalog/ActivationData.cs)。
