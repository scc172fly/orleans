---
title: 客户端配置
description: 配置 Orleans 外部客户端。
ms.date: 08/02/2026
ms.topic: how-to
---

<a id="client-configuration"></a>

# 客户端配置

外部客户端在 Silo 进程之外运行，并通过 Silo 网关访问集群。安装 [Microsoft.Orleans.Client](https://www.nuget.org/packages/Microsoft.Orleans.Client)，调用 <xref:Microsoft.Extensions.Hosting.OrleansClientGenericHostExtensions.UseOrleansClient*>，并配置与 Silo 相同的集群标识和集群提供程序：

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="external_client":::

客户端设置遵循 [.NET 选项模式](https://learn.microsoft.com/dotnet/core/extensions/options)。宿主在后续注册的托管服务之前启动 Orleans 客户端，并与应用程序的其他部分一起停止它。从 <xref:Orleans.IClusterClient> 或 <xref:Orleans.IGrainFactory> 所表示的服务中按需选择，通过 [.NET 依赖注入](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection)解析；不要手动构建第二个客户端单例，也不要为每个请求创建新的客户端。

对于 ASP.NET Core 及其他通用宿主应用程序，在进程生命周期内保留单个客户端实例。这就是注入到控制器、后台工作器、托管服务及其他长生命周期组件中的同一个客户端。宿主负责启动和关闭；不要释放通过依赖注入获得的单例，也不要用静态缓存替代它。

<a id="orleans-clustering-information"></a>

## Orleans 集群信息

- <xref:Orleans.Configuration.ClusterOptions.ServiceId> 标识逻辑 Orleans 应用程序，应保持稳定。
- <xref:Orleans.Configuration.ClusterOptions.ClusterId> 标识该服务的一次部署。使用不同值来隔离环境或并行部署。
- 客户端集群提供程序发现已启用网关的 Silo。其设置必须指向与 Silo 相同的成员资格数据。

<a id="clustering-provider"></a>

### 集群提供程序

常见生产环境[集群程序包](server-configuration.md#clustering-provider)包括 Azure Table Storage、ADO.NET、Redis、Azure Cosmos DB、DynamoDB、Consul 和 ZooKeeper。静态集群和 localhost 集群用于开发。Kubernetes 托管是 Silo 集成，而非客户端集群提供程序。

当 Aspire 提供 Orleans 资源时，注册对应的键控服务客户端，并使用无参数形式：

:::code language="csharp" source="../snippets/aspire/Client/ClientProgram.cs" id="client_basic_config":::

<a id="connection-resiliency"></a>

## 连接弹性

Orleans 注册默认的 <xref:Orleans.IClientConnectionRetryFilter>。它使用线性退避重试符合条件的初始连接失败，最多重试 15 次。如果客户端仍然无法连接、宿主正在停止，或故障被判定为不可重试，则宿主启动失败。

仅当应用程序有不同的启动要求时覆盖该策略：

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="client_retry":::

为每个自定义重试策略设置上限并遵循取消令牌，使部署能够快速失败，关闭也不会被无限期拖延。

初始连接重试不会使 Grain 调用具有幂等性。调用可能在目标已开始处理后失败，因此只有应用程序操作的语义允许重复执行时才重试。暂时性连接故障发生后，Grain 引用仍然可用。

<a id="gateway-behavior"></a>

## 网关行为

通过 <xref:Orleans.Configuration.GatewayOptions> 或 `Orleans:Gateway` 配置网关刷新及连接行为。Orleans 从集群提供程序刷新网关列表，并在网关不可用时重新连接。仅向需要访问的客户端网络公开网关终结点；Silo 之间的通信使用独立的终结点。

对于共同托管的客户端，改用 <xref:Microsoft.Extensions.Hosting.OrleansSiloGenericHostExtensions.UseOrleans*>。Silo 的客户端直接与集群通信，无须经过网关跳转。
