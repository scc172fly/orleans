---
title: 本地开发配置
description: 为本地开发配置 Orleans 应用程序。
ms.date: 08/02/2026
ms.topic: how-to
---

<a id="local-development-configuration"></a>

# 本地开发配置

<a id="silo-configuration"></a>

## Silo 配置

为了获得最短的开发迭代周期，将 Grain 和调用代码托管在同一个进程中，并使用 localhost 集群配置：

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="local_silo_and_client":::

<xref:Microsoft.Extensions.Hosting.OrleansSiloGenericHostExtensions.UseOrleans*> 注册同宿主的 <xref:Orleans.IClusterClient>，因此控制器、终结点、托管服务及其他通过依赖注入获得依赖项的组件，无需单独的客户端进程即可调用 Grain。

<xref:Orleans.Hosting.CoreHostingExtensions.UseLocalhostClustering*> 配置环回网络和开发集群。内存存储和内存提醒器也仅供开发使用：进程停止时，其中的数据会丢失。

<a id="client-configuration"></a>

## 客户端配置

当生产环境架构要求隔离客户端和 Silo 时，使用单独的进程：

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="local_external_client":::

客户端与 Silo 必须使用匹配的网关端口、<xref:Orleans.Configuration.ClusterOptions.ServiceId> 和 <xref:Orleans.Configuration.ClusterOptions.ClusterId>。当两者运行在同一台计算机上时，<xref:Orleans.Hosting.ClientBuilderExtensions.UseLocalhostClustering*> 提供匹配的默认值。

<a id="run-multiple-local-silos"></a>

## 运行多个本地 Silo

<xref:Orleans.Hosting.CoreHostingExtensions.UseLocalhostClustering*> 针对单个本地 Silo 进行了优化。若要测试成员资格变更或故障转移，请使用以下方式之一：

- 使用 [Aspire](../aspire-integration.md)，结合容器化 Redis 或其他受支持的集群资源，以及多个 Silo 副本。
- 配置共享的开发集群主 Silo，并为各个 Silo 分配唯一的 Silo 端口和网关端口。
- 使用与生产环境相同的集群提供程序，连接到本地容器或模拟器。

开发集群将成员资格表保存在主 Silo 的内存中，成员资格版本在整个集群生命周期内持续递增。丢失该表会导致集群失效；必须重启所有 Silo 以建立新集群。如果存活的 Silo 读取到的成员资格表版本低于它已知的成员资格版本，就会调用 <xref:Orleans.Runtime.IFatalErrorHandler.OnFatalException*>。对于重叠的读取操作，比较时使用每次读取开始时已知的版本。

Aspire 通常是最简单的选择，因为它可以分配终结点、启动依赖项、注入配置并显示每个副本的日志。

> [!IMPORTANT]
> 不要在生产环境中部署 localhost、静态、开发、内存或由模拟器支持的提供程序作为基础设施。它们无法提供多节点部署所需的持久性和可用性。

<a id="choose-local-backing-services"></a>

## 选择本地后端服务

使用内存提供程序进行快速的单元级迭代。需要测试提供程序行为、序列化格式、架构初始化或跨重启的持久性时，使用容器或模拟器。在可行的情况下，保持提供程序包及配置结构与生产环境一致。

对于创建进程内集群的集成测试，使用 [`Microsoft.Orleans.TestingHost`](https://www.nuget.org/packages/Microsoft.Orleans.TestingHost) 程序包和 <xref:Orleans.TestingHost.TestClusterBuilder>，而不是手动分配端口。有关完整的应用程序，请参阅 [Orleans 示例](https://github.com/dotnet/orleans/tree/main/samples)。
