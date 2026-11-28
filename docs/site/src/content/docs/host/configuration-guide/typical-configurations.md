---
title: 典型 Orleans 配置
description: 选择本地开发、Aspire 或生产环境的 Orleans 配置。
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="typical-orleans-configurations"></a>

# 典型 Orleans 配置

选择能够满足部署要求的最精简宿主模式。

| 场景 | 宿主模式 | 集群配置 | 状态和提醒器 |
|---|---|---|---|
| 单进程开发 | Silo 和客户端位于同一宿主 | <xref:Orleans.Hosting.CoreHostingExtensions.UseLocalhostClustering*> | 内存提供程序 |
| 本地分布式开发 | Aspire 与多个 Silo 副本 | 本地容器或模拟器 | 本地容器或模拟器 |
| 带 HTTP/API 入口的生产服务 | 无需资源隔离时，在每个 Silo 中共同承载 ASP.NET Core 和 Orleans | 适合平台的持久化提供程序 | 持久化提供程序 |
| 隔离的前端与工作层 | 前端使用外部 Orleans 客户端，工作层仅承载 Silo | 两层使用相同的持久化提供程序和集群标识 | Silo 上的持久化提供程序 |

<a id="local-development"></a>

## 本地开发

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="local_silo_and_client":::

此配置有意设计为可丢弃环境。添加更多本地 Silo 之前，请参阅[本地开发配置](local-development-configuration.md)。

<a id="recommended-aspire-configuration"></a>

## 推荐：Aspire 配置

可编译的 AppHost 示例定义了 Orleans 资源及其依赖项：

:::code language="csharp" source="../snippets/aspire/AppHost/AppHostExamples.cs" id="orleans_with_storage_reminders":::

Silo 注册 Aspire 服务客户端，并让 Orleans 使用注入的配置：

:::code language="csharp" source="../snippets/aspire/Silo/SiloProgram.cs" id="silo_basic_config":::

使用 `.WithReplicas(...)` 建模多个 Silo。本地 Redis 容器和 Azurite 是有用的开发依赖项，但生产部署必须将这些资源绑定到托管服务或其他具备持久性的服务。不要在生产 AppHost 配置中调用 `.RunAsEmulator()`。

<a id="production-configuration"></a>

## 生产环境配置

<a id="production-configuration-with-redis"></a>
<a id="production-configuration-with-azure-storage"></a>
<a id="reliable-production-deployment-using-azure"></a>
<a id="reliable-production-deployment-using-sql-server"></a>
<a id="traditional-configurations-without-aspire"></a>

生产环境配置应显式做出以下选择：

1. 选择平台支持的持久化集群提供程序。
2. 设置稳定的 <xref:Orleans.Configuration.ClusterOptions.ServiceId>，以及特定于环境或部署的 <xref:Orleans.Configuration.ClusterOptions.ClusterId> 值。
3. 配置所有 Silo 和客户端均可路由访问的公布地址。
4. 仅针对应用程序实际使用的功能添加持久化存储、提醒器、流及 Grain 目录。
5. 通过部署环境提供凭据，优先使用工作负载标识。
6. 配置健康及就绪检查、遥测、优雅终止、CPU、内存及服务器 GC。

例如，Azure 部署可使用 Azure Table Storage 实现集群及提醒器，并使用 Azure Blob 或 Table Storage 存储 Grain 状态。AWS 部署可使用 DynamoDB。以数据库为中心的部署可使用 ADO.NET。Redis、Cosmos DB、Consul 和 ZooKeeper 提供程序也可用于其各自包所实现的功能。Kubernetes 部署可结合独立的 Kubernetes 宿主集成和其中一个集群提供程序使用。

集群提供程序不必与 Grain 存储或提醒器提供程序相同。应根据持久性、延迟、运维责任及成本分别选择。

<a id="separate-external-client"></a>

## 独立外部客户端

当前端与 Silo 层需要独立伸缩、安全边界、部署或资源隔离时，使用外部客户端。配置 <xref:Microsoft.Extensions.Hosting.OrleansClientGenericHostExtensions.UseOrleansClient*>，使其服务标识、集群标识和集群后端与 Silo 层完全相同。客户端访问网关终结点，因此应将网关路由与 Silo 间终结点分开公开及保护。

<a id="avoid-development-configuration-in-production"></a>

## 避免在生产环境使用开发配置

不要在生产环境使用以下任何配置：

- <xref:Orleans.Hosting.CoreHostingExtensions.UseLocalhostClustering*>、开发环境集群或静态网关列表。
- 数据必须持久保留时使用内存 Grain 存储或内存提醒器。
- Azurite 或其他模拟器终结点。
- 将环回地址或通配符地址用作公布的终结点。
- 无界的自定义客户端连接重试。

有关实现细节，请参阅[服务端配置](server-configuration.md)、[客户端配置](client-configuration.md)和 [Orleans 与 Aspire 集成](../aspire-integration.md)。
