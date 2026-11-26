---
title: "Grain 接口版本"
description: "路线路线 grain 不同调用时 Orleans 使用数字界面版本的部署。"
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="grain-interface-versioning"></a>

# Grain 接口版本

Grain 接口版本在部署期间使不同界面版本的 silos 并存。它是一个 **数字路由和定位政策**。 它不使用持久性状态, 验证两个 .NET 接口的结构兼容性, 或者迁移数据 。

<a id="assign-a-version"></a>

## 指定版本

应用 <xref:Orleans.CodeGeneration.VersionAttribute> 到 grain 接口 :

:::code language="csharp" source="../../snippets/compiled/Grains/RequestsAndVersioningSnippets.cs" id="versioned_cart_interface":::
值为无符号 16 位整数。 没有属性的界面有版本 `0`。 连续修订合同时使用单元增长值 。

<a id="how-routing-works"></a>

## 路线安排如何运作

每个请求版本都含有其调用者所使用的数字界面版本。 Orleans:

1. 读取支持版本 silos 为此, grain 接口。
1. 应用 a [兼容战略](compatible-grains.md) 来确定哪个激活版本可以处理请求的版本。
1. 当新激活需要放置时, 应用 [选择器战略](version-selector-strategy.md) 。
1. 将请求的路径移到一个支持选定版本之一的 silo 。

如果请求达到现有启动程序,且其版本不兼容, Orleans 以理由 `IncompatibleRequest`停止运行,则取消失效地址,并重试兼容激活程序的放置。

> [!IMPORTANT]
> 运行时兼容性检查仅比较版本编号。 Orleans 不检查方法、参数类型、序列器合同或行为以证明兼容性。 应用程序必须维护选定战略所代表的合同。 开发过程中, 请选择使用 [Orleans 合同兼容性分析器](contract-compatibility-analyzer.md) 来跟踪源控制中的 RPC 签名和身份 。

<a id="configure-cluster-defaults"></a>

## 配置组群默认

<xref:Orleans.Configuration.GrainVersioningOptions> defaults to <xref:Orleans.Versions.Compatibility.BackwardCompatible> and <xref:Orleans.Versions.Selector.AllCompatibleVersions>:

:::code language="csharp" source="./snippets/versioning/VersioningConfiguration.cs" id="configure_versioning":::

配置的战略名称解析已注册 Orleans 战略服务。 在异式部署之前, 以一致的方式配置每个 silo 。

Orleans 也暴露了运行时间战略的变化, <xref:Orleans.IVersionManager>管理层执行的 grain.变化可适用于整个集群或特定 <xref:Orleans.Runtime.GrainInterfaceType>。运行时超常是运行状态:仔细协调,重置为部署后设置的默认。

<a id="scope-and-limitations"></a>

## 范围和限制

- 无国籍工人 grains 没有被版本。
- 流式界面没有版本 。
- 国家和储存计划演变是单独的责任。
- 版本路由只会帮助所有部署的履约方 遵守已宣布的兼容合同

滚升序列见 [部署新版本 grain](deploying-new-versions-of-grains.md) 。
