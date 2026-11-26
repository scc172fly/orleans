---
title: "典型 Orleans 配置"
description: "选择本地、 占卜或生产 Orleans 配置 。"
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="typical-orleans-configurations"></a>

# 典型 Orleans 配置

选择与部署匹配的最小主机模式 。

| 设想情况 | 东道主模式 | 集群组合 | 国家及催复通知 |
|---|---|---|---|
| 单过程开发 | 共同主办 silo 和客户 | <xref:Orleans.Hosting.CoreHostingExtensions.UseLocalhostClustering*> | 内存供应商 |
| 地方分配发展 | 具有多个 silo 复制件的提醒 | 本地集装箱或模拟器 | 本地集装箱或模拟器 |
| 与HTTP/API 入境点提供生产服务 | Co-host ASP.NET 核心和核心 Orleans 在每个 silo 不需要资源孤立 | 平台适当、耐久提供方 | 持久供应者 |
| 孤立的前端和工人级 | 前端的外部 Orleans 客户端; silo- 仅工人级 | 两个层级的同一耐久提供方和集群特征 | silos 上的持久解决办法提供者  |

<a id="local-development"></a>

## 地方发展

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="local_silo_and_client":::

这是故意可支配的。 [地方发展配置](local-development-configuration.md) 在添加更多本地 silos.

<a id="recommended-aspire-configuration"></a>

## 建议: 临时配置

汇编的 AppHost 示例界定了 Orleans 资源及其依赖性:

:::code language="csharp" source="../snippets/aspire/AppHost/AppHostExamples.cs" id="orleans_with_storage_reminders":::

silo 登记了服务客户,并允许 Orleans 使用注射配置:

:::code language="csharp" source="../snippets/aspire/Silo/SiloProgram.cs" id="silo_basic_config":::

使用 `.WithReplicas(...)` 模型化多 silos当地 Redis Azurite集装箱和Azurite集装箱是有用的发展依赖,但生产部署必须把这些资源与管理或持久服务捆绑在一起。 `.RunAsEmulator()` 生产过程中 AppHost 配置。

<a id="production-configuration"></a>

## 生产配置

<a id="production-configuration-with-redis"></a>
<a id="production-configuration-with-azure-storage"></a>
<a id="reliable-production-deployment-using-azure"></a>
<a id="reliable-production-deployment-using-sql-server"></a>
<a id="traditional-configurations-without-aspire"></a>

生产配置应明确作出这些选择:

1. 挑选平台支持的持久集群提供者。
2. 设定稳定 <xref:Orleans.Configuration.ClusterOptions.ServiceId> 和环境/部署特定 <xref:Orleans.Configuration.ClusterOptions.ClusterId> 值。
3. 配置已发布每个 silo 和客户可以访问的地址 。
4. 仅为应用程序的功能添加耐久存储、提醒、串流和 grain 目录。
5. 通过部署环境提供供应证书,最好使用工作量身份。
6. 配置健康/准备状态、遥测、优雅终止、CPU、内存和服务器GC。

例如, Azure 部署可以使用 Azure 表格存储来进行组合和提醒, Azure 布局或表存储可以用于 grain 状态。AWS部署可以使用 DynamoDB。数据库中心部署可以使用 ADO.NET。 Redis、宇宙DB、领事和 ZooKeeper 供应商也可以使用其软件包实施的能力。 Kubernetes 部署可以使用与这些集群提供者之一分开的 Kubernetes 主机整合。

用于分组的供应商不需要与 grain 存储或提醒提供者匹配。根据耐用性、延迟性、操作所有权和成本,选择每种方式。

<a id="separate-external-client"></a>

## 独立的外部客户端

前端和 silo 需要分级进行缩放、安全边界、部署或资源隔离。 <xref:Microsoft.Extensions.Hosting.OrleansClientGenericHostExtensions.UseOrleansClient*> 与 silo 客户到达网关端点,从而暴露和确保这些路线与 silo- 来...silo 终点。

<a id="avoid-development-configuration-in-production"></a>

## 避免生产中的发展配置

生产时不得使用下列任何一种:

- <xref:Orleans.Hosting.CoreHostingExtensions.UseLocalhostClustering*>、发展集群或静态网关清单。
- 当数据必须存在时, 内存 grain 存储或记忆提醒 。
- 祖瑞特或其他模拟器终点点。
- 循环支持或通配符地址作为广告的终点。
- 不受约束的客户端连接重试 。

执行详情见 [服务器配置](server-configuration.md)、 [客户配置](client-configuration.md)和 [Orleans 和指定集成](../aspire-integration.md) 。
