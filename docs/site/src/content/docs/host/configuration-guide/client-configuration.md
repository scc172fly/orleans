---
title: "客户配置"
description: "配置 Orleans 外部客户端。"
ms.date: 08/02/2026
ms.topic: how-to
---

<a id="client-configuration"></a>

# 客户配置

外部客户端在 silo 进程之外运行,并通过 silo 网关到达集群。安装 [Microsoft.Orleans.Client](https://www.nuget.org/packages/Microsoft.Orleans.Client),调用 <xref:Microsoft.Extensions.Hosting.OrleansClientGenericHostExtensions.UseOrleansClient*>,配置与 silos相同的群集身份和群集提供者:

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="external_client":::

客户参与 [.NET 选项模式](https://learn.microsoft.com/dotnet/core/extensions/options)。主机开始 Orleans 客户端在后来登记托管服务之前,先注册托管服务,然后与应用程序的其余部分停止服务。 <xref:Orleans.IClusterClient> 或 <xref:Orleans.IGrainFactory> 调自 [.NET 依赖性注射](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection);不要手工创建第二个客户端单键,或者根据请求创建一个新的客户端。

对于 ASP.NET 核心应用程序和其他通用主机应用程序,在程序寿命期间保留一个客户实例。 这是您输入控制器、背景工人、托管服务和其他长寿部件的同一客户。 东道方拥有启动和关闭功能; 不处置依赖性注入单吨或用静态缓存替换它 。

<a id="orleans-clustering-information"></a>

## Orleans 群集信息

- <xref:Orleans.Configuration.ClusterOptions.ServiceId> identifies the logical Orleans application and should remain stable.
- <xref:Orleans.Configuration.ClusterOptions.ClusterId> identifies one deployment of that service. Use a different value to isolate environments or parallel deployments.
- 客户群集提供者发现有网关功能的 silos。其设置必须指向与 silos相同的成员数据。

<a id="clustering-provider"></a>

### 集群集集供应商

通用生产 [组合组合套包](server-configuration.md#clustering-provider) 包括 Azure 表格储存、 ADO.NET、 Redis、 Azure 宇宙DB、 DynamoDB、领事和 ZooKeeper。静态和本地主机集群是用于开发的。 Kubernetes 托管是一个 silo 整合,而不是客户集群提供者。

当 Aspire 提供 Orleans 资源时, 请注册相应的按键服务客户端并使用无参数表单:

:::code language="csharp" source="../snippets/aspire/Client/ClientProgram.cs" id="client_basic_config":::

<a id="connection-resiliency"></a>

## 连接相继性

Orleans 登记了一个默认 <xref:Orleans.IClientConnectionRetryFilter>。 它会重新输入符合初始连接失败条件的线性回溯, 最多15次重试。 如果客户端仍然无法连接、 主机正在停止或者失败被视为不可重试, 则主机启动失败 。

仅在应用程序有不同的启动要求时才覆盖政策:

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="client_retry":::

打破每一个习惯重试政策 并尊重取消的象征 这样部署可以快速失败 并且关闭不会无限期推迟

初始连接重写不会使 grain 调用无效。 目标开始处理后, 呼叫可能失败, 所以只有在它们的语义能容忍重复时才能重新尝试应用程序操作 。 Grain 引用在短暂连接故障后仍然可用 。

<a id="gateway-behavior"></a>

## 网关行为

配置网关更新和连接行为, 通过 <xref:Orleans.Configuration.GatewayOptions> 或 `Orleans:Gateway`。 Orleans 将网关列表从集群提供者中刷新, 当网关无法使用时重新连接。 仅向客户网络提供端点, 用户网络需要这些端点; silo-silo 流量使用单独的端点 。

对于联合托管的客户端, 请使用 <xref:Microsoft.Extensions.Hosting.OrleansSiloGenericHostExtensions.UseOrleans*> 。 silo的客户端直接与该组直接沟通, 不需要连接跳 。
