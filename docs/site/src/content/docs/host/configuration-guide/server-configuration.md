---
title: "服务器配置"
description: "配置 Orleans silos、提供者和网络端点。"
ms.date: 08/15/2026
ms.topic: how-to
---

<a id="server-configuration"></a>

# 服务器配置

安装 [Microsoft.Orleans.Server](https://www.nuget.org/packages/Microsoft.Orleans.Server) ,并将 Orleans 添加到 [.NET 通用主机](https://learn.microsoft.com/dotnet/core/extensions/generic-host):

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="redis_silo":::

<xref:Microsoft.Extensions.Hosting.OrleansSiloGenericHostExtensions.UseOrleans*> 主主主机 a silo 登记并登记了一名共同主办的 <xref:Orleans.IClusterClient>使用 <xref:Microsoft.Extensions.Hosting.OrleansClientGenericHostExtensions.UseOrleansClient*> 仅在一个不主机的进程中 grains.

<a id="clustering-provider"></a>

## 集群集集供应商

每个 silo 和外部客户端必须使用相同的 <xref:Orleans.Configuration.ClusterOptions.ServiceId>、 <xref:Orleans.Configuration.ClusterOptions.ClusterId>和组合后端。安装部署平台的软件包:

| 后端 | 典型包件或集成 | 注注注注注注注注注注注注型小小小小小小小 |
|---|---|---|
| Azure 表格储存 | [`Microsoft.Orleans.Clustering.AzureStorage`](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.AzureStorage) | 支持 Microsoft 添加证书和连接字符串。 |
| ADO.NET | [`Microsoft.Orleans.Clustering.AdoNet`](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.AdoNet) | 支持 SQL 服务器、 PostgreSQL、 MySQL/MariaDB和甲骨文。 |
| Redis | [`Microsoft.Orleans.Clustering.Redis`](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.Redis) | 能够共享管理或自办的 Redis 服务。 |
| Azure 宇宙 DB | [`Microsoft.Orleans.Clustering.Cosmos`](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.Cosmos) | a 使用宇宙DB集装箱加入。 |
| DynamoDB | [`Microsoft.Orleans.Clustering.DynamoDB`](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.DynamoDB) | 用于AWS部署的常见方法。 |
| 领事领事领事 | [`Microsoft.Orleans.Clustering.Consul`](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.Consul) | 使用领事钥匙/价值存储。 |
| ZooKeeper | [`Microsoft.Orleans.Clustering.ZooKeeper`](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.ZooKeeper) | 使用 ZooKeeper 合用。 |

仅使用 <xref:Orleans.Hosting.CoreHostingExtensions.UseLocalhostClustering*>、开发集群或静态网关进行当地开发和测试。

[`Microsoft.Orleans.Hosting.Kubernetes`](https://www.nuget.org/packages/Microsoft.Orleans.Hosting.Kubernetes) 配置一个 silo ,从舱底环境到 <xref:Orleans.Hosting.KubernetesHostingExtensions.UseKubernetesHosting*>;它不是一个集群提供者。 Kubernetes 部署仍然需要上面的一个共享集群提供者。

组合存储群, 不是 grain 状态。 在应用程序使用时配置 grain 存储并单独提醒 。 供应商软件包暴露了 `Use...Clustering`、 `Add...GrainStorage`和 `Use...ReminderService` 方法, 也可以参与 [声明性配置](index.md#declarative-configuration)。

<a id="orleans-clustering-information"></a>

## Orleans 群集信息

<xref:Orleans.Configuration.ClusterOptions.ServiceId> identifies the logical application and namespaces provider data. Keep it stable for the lifetime of the application. <xref:Orleans.Configuration.ClusterOptions.ClusterId> identifies a specific cluster, such as `orders-production` or `orders-green`. All participants in one cluster must agree on both values.

<a id="endpoints"></a>

## 终点

silo 有两个已公布的终点:

- silo 端点用于 silo-至silo 的交通。其默认端口是 `11111`。
- 网关端点为外部客户端使用。 其默认端口是 `30000`; 将其设为 `0` 以禁用网关 。

Orleans 还必须知道广告的IP地址。 <xref:Orleans.Configuration.EndpointOptions.AdvertisedIPAddress> 未被配置, Orleans 选择本地地址,必要时返回回溯。 监听端点默认为广告地址和相应的广告端口; Orleans 是否 **否** 听每个界面,除非您配置通配符倾听端点。

对于可直接访问的主机,帮助者配置了广告上的端口和地址:

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="direct_endpoints":::

对于集装箱、国家税务局或港口转运,独立配置广告和监听端点:

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="advertised_and_listening_endpoints":::

此 silo 在港口 `40000` 和 `50000` 上收听, 但公布 `172.16.0.42:11111` 和 `172.16.0.42:30000`。 确保会员数据永远不包含同行无法访问的地址 。

关于私人联网、主机港制图和交叉主机集装箱诊断,见 [运行 Orleans 多个主机的集装箱](../../deployment/containers.md)。

<a id="configure-providers-and-options"></a>

## 配置提供者和选项

当 grain 类型需要不同的仓库时使用命名的提供者:

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="named_providers":::

配置使用选项模式的运行时行为 :

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="membership_options":::

最好在测量或部署要求证明改变有正当理由之前,先发生违约。见 [核心配置备选方案](list-of-options-classes.md) ,而不是将每个属性复制为应用程序配置。

<a id="production-guidance"></a>

## 生产指南

- 使用工作量身份、管理下的身份,或供应商支持的另一种短期证书机制。
- 将连接字符串和证书保留在源控制之外。
- 当可用性要求要求达到法定人数式的不容失率时,在故障领域之间至少运行三个 silos 。
- 配置就绪状态, 因此流量只能在主机启动完成后开始 。
- 让通用主机接收终止信号并完成 [宽限期关闭](shutting-down-orleans.md)。
- 从有代表性的负载测试中选择 [.NET GC 模式](configuring-garbage-collection.md) 和大小 CPU/模拟限制。
