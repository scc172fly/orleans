---
title: Grain 接口版本控制
description: 使用数字接口版本在异构 Orleans 部署期间路由 Grain 调用。
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="grain-interface-versioning"></a>

# Grain 接口版本控制

Grain 接口版本控制允许具有不同接口版本的 Silo 在部署期间共存。它是一种**基于数字版本的路由与放置策略**，不会为持久化状态进行版本控制，不会验证两个 .NET 接口的结构兼容性，也不会迁移数据。

<a id="assign-a-version"></a>

## 指定版本

将 <xref:Orleans.CodeGeneration.VersionAttribute> 应用于 Grain 接口：

:::code language="csharp" source="../../snippets/compiled/Grains/RequestsAndVersioningSnippets.cs" id="versioned_cart_interface":::
该值是无符号 16 位整数。没有此特性的接口版本为 `0`。应为后续契约修订使用单调递增的版本号。

<a id="how-routing-works"></a>

## 路由工作原理

每个带版本的请求都携带调用方使用的数字接口版本。Orleans 会：

1. 读取各 Silo 支持的该 Grain 接口的版本。
1. 应用 [兼容性策略](compatible-grains.md)，确定哪些版本的激活实例能够处理所请求的版本。
1. 在需要放置新的激活实例时应用 [选择器策略](version-selector-strategy.md)。
1. 将请求路由至支持其中一个所选版本的 Silo。

如果请求到达版本不兼容的现有激活实例，Orleans 会以 `IncompatibleRequest` 为原因停用该实例，使陈旧地址失效，并重新尝试放置兼容的激活实例。

> [!IMPORTANT]
> 运行时兼容性检查仅比较版本号。Orleans 不会检查方法、参数类型、序列化器契约或行为来证明兼容性。应用程序必须遵守所选策略代表的契约。在开发期间，可选择启用 [Orleans 契约兼容性分析器](contract-compatibility-analyzer.md)，在源码管理中跟踪 RPC 签名和标识。

<a id="configure-cluster-defaults"></a>

## 配置集群默认值

<xref:Orleans.Configuration.GrainVersioningOptions> 的默认策略为 <xref:Orleans.Versions.Compatibility.BackwardCompatible> 和 <xref:Orleans.Versions.Selector.AllCompatibleVersions>：

:::code language="csharp" source="./snippets/versioning/VersioningConfiguration.cs" id="configure_versioning":::

配置的策略名称用于解析已注册的 Orleans 策略服务。在异构部署前，应一致地配置所有 Silo。

Orleans 还通过管理 Grain 实现的 <xref:Orleans.IVersionManager> 提供运行时策略变更能力。变更可以应用到整个集群或指定的 <xref:Orleans.Runtime.GrainInterfaceType>。运行时覆盖项属于运维状态：应谨慎协调这些变更，并在部署后将其重置为配置的默认值。

<a id="scope-and-limitations"></a>

## 适用范围与限制

- 无状态工作者 Grain 不支持版本控制。
- 流处理接口不支持版本控制。
- 状态及存储架构演进是独立的职责。
- 只有当所有已部署的实现都遵守声明的兼容性契约时，版本路由才有帮助。

有关滚动升级顺序，请参阅 [部署新版本的 Grain](deploying-new-versions-of-grains.md)。
