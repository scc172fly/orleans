---
title: 服务端配置
description: 配置 Orleans Silo、提供程序和网络终结点。
ms.date: 08/15/2026
ms.topic: how-to
---

<a id="server-configuration"></a>

# 服务端配置

安装 [Microsoft.Orleans.Server](https://www.nuget.org/packages/Microsoft.Orleans.Server)，并将 Orleans 添加到 [.NET 通用宿主](https://learn.microsoft.com/dotnet/core/extensions/generic-host)：

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="redis_silo":::

<xref:Microsoft.Extensions.Hosting.OrleansSiloGenericHostExtensions.UseOrleans*> 承载 Silo，并注册同宿主的 <xref:Orleans.IClusterClient>。仅在不承载 Grain 的进程中使用 <xref:Microsoft.Extensions.Hosting.OrleansClientGenericHostExtensions.UseOrleansClient*>。

<a id="clustering-provider"></a>

## 集群提供程序

每个 Silo 和外部客户端都必须使用相同的 <xref:Orleans.Configuration.ClusterOptions.ServiceId>、<xref:Orleans.Configuration.ClusterOptions.ClusterId> 和集群后端。安装适合部署平台的包：

| 后端 | 常用包或集成 | 说明 |
|---|---|---|
| Azure Table Storage | [`Microsoft.Orleans.Clustering.AzureStorage`](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.AzureStorage) | 支持 Microsoft Entra 凭据和连接字符串。 |
| ADO.NET | [`Microsoft.Orleans.Clustering.AdoNet`](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.AdoNet) | 支持 SQL Server、PostgreSQL、MySQL/MariaDB 和 Oracle。 |
| Redis | [`Microsoft.Orleans.Clustering.Redis`](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.Redis) | 可以共用托管或自建的 Redis 服务。 |
| Azure Cosmos DB | [`Microsoft.Orleans.Clustering.Cosmos`](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.Cosmos) | 使用 Cosmos DB 容器保存成员资格数据。 |
| DynamoDB | [`Microsoft.Orleans.Clustering.DynamoDB`](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.DynamoDB) | 常用于 AWS 部署。 |
| Consul | [`Microsoft.Orleans.Clustering.Consul`](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.Consul) | 使用 Consul 键值存储。 |
| ZooKeeper | [`Microsoft.Orleans.Clustering.ZooKeeper`](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.ZooKeeper) | 使用 ZooKeeper 集群。 |

仅在本地开发和测试时使用 <xref:Orleans.Hosting.CoreHostingExtensions.UseLocalhostClustering*>、开发环境集群或静态网关。

[`Microsoft.Orleans.Hosting.Kubernetes`](https://www.nuget.org/packages/Microsoft.Orleans.Hosting.Kubernetes) 通过 <xref:Orleans.Hosting.KubernetesHostingExtensions.UseKubernetesHosting*> 根据 Pod 环境配置 Silo；它不是集群提供程序。Kubernetes 部署仍需使用上述共享集群提供程序之一。

集群提供程序存储成员资格，而不是 Grain 状态。如果应用程序使用 Grain 存储和提醒器，应分别配置。提供程序包公开 `Use...Clustering`、`Add...GrainStorage` 和 `Use...ReminderService` 方法，也可参与[声明式配置](index.md#declarative-configuration)。

<a id="orleans-clustering-information"></a>

## Orleans 集群信息

<xref:Orleans.Configuration.ClusterOptions.ServiceId> 标识逻辑应用程序，并为提供程序数据划分命名空间。在应用程序的整个生命周期中应保持此值稳定。<xref:Orleans.Configuration.ClusterOptions.ClusterId> 标识特定集群，例如 `orders-production` 或 `orders-green`。同一集群中的所有参与者必须使用相同的两个值。

<a id="endpoints"></a>

## 终结点

Silo 有两个对外公布的终结点：

- Silo 终结点用于 Silo 间通信，默认端口为 `11111`。
- 网关终结点供外部客户端使用，默认端口为 `30000`；设为 `0` 可禁用网关。

Orleans 还必须知道要公布的 IP 地址。如果未配置 <xref:Orleans.Configuration.EndpointOptions.AdvertisedIPAddress>，Orleans 会选择本地地址，必要时回退到环回地址。默认监听终结点使用公布的地址和相应端口；除非配置通配符监听终结点，否则 Orleans **不会**监听所有网络接口。

对于可直接访问的宿主，可通过辅助方法配置公布的端口及地址：

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="direct_endpoints":::

对于容器、NAT 或端口转发，应分别配置公布的终结点与监听终结点：

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="advertised_and_listening_endpoints":::

此 Silo 监听端口 `40000` 和 `50000`，但公布 `172.16.0.42:11111` 和 `172.16.0.42:30000`。确保成员资格数据中绝不包含对端无法路由访问的地址。

有关私有网络、宿主端口映射及跨宿主容器诊断，请参阅[在多个宿主的容器中运行 Orleans](../../deployment/containers.md)。

<a id="configure-providers-and-options"></a>

## 配置提供程序和选项

当不同 Grain 类型需要不同存储时，使用命名提供程序：

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="named_providers":::

使用选项模式配置运行时行为：

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="membership_options":::

优先采用默认值，直到测量结果或部署要求表明确有调整必要。请参阅[核心配置选项](list-of-options-classes.md)，而不是将所有属性复制到应用程序配置中。

<a id="production-guidance"></a>

## 生产环境建议

- 在提供程序支持的情况下，使用工作负载标识、托管标识或其他短期凭据机制。
- 不要将连接字符串及凭据纳入源代码版本控制。
- 当可用性要求需要类似法定人数的容错能力时，在不同故障域中至少运行三个 Silo。
- 配置就绪检查，使宿主启动完成后才开始接收流量。
- 让通用宿主接收终止信号并完成[优雅关闭](shutting-down-orleans.md)。
- 根据具有代表性的负载测试选择 [.NET GC 模式](configuring-garbage-collection.md)，并确定 CPU 和内存限制。
