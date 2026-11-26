---
title: "地方发展配置"
description: "配置本地开发的 Orleans 应用程序 。"
ms.date: 08/02/2026
ms.topic: how-to
---

<a id="local-development-configuration"></a>

# 地方发展配置

<a id="silo-configuration"></a>

## Silo 配置

对于最短的发展环,主机 grains 和调用码在一个过程中进行,并使用本地主机群集:

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="local_silo_and_client":::

<xref:Microsoft.Extensions.Hosting.OrleansSiloGenericHostExtensions.UseOrleans*> 登记一个共同主办的 <xref:Orleans.IClusterClient>,因此控制器、终端点、主机服务和其他依赖性注射部件可不通过单独的客户程序调用 grains 。

<xref:Orleans.Hosting.CoreHostingExtensions.UseLocalhostClustering*> 配置回路网络和开发群集。 记忆存储和记忆提醒也只用于开发: 当进程停止时, 它们的数据丢失 。

<a id="client-configuration"></a>

## 客户配置

当生产结构需要客户和 silo 隔离时,使用单独的程序:

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="local_external_client":::

客户端和 silo 必须使用匹配的网关端口、 <xref:Orleans.Configuration.ClusterOptions.ServiceId>和 <xref:Orleans.Configuration.ClusterOptions.ClusterId>。 当两个都在同一台机器运行时, <xref:Orleans.Hosting.ClientBuilderExtensions.UseLocalhostClustering*> 的供给要匹配默认值。

<a id="run-multiple-local-silos"></a>

## 运行多个本地 silos

<xref:Orleans.Hosting.CoreHostingExtensions.UseLocalhostClustering*> 优化为单一本地 silo。为了实行会籍变更或倒闭,使用其中一种方法:

- 使用 [指定](../aspire-integration.md) ,同时使用一个集装箱化 Redis 或其他支持的组合资源以及多个 silo 复制品。
- 配置一个共享开发组合主机,并指定每个 silo 独有的 silo 和网关端口。
- 同一个生产组群供应商对当地集装箱或模拟器运行。

发展集群将其成员表保留在原始 silo的记忆中,成员版本在整个该组的存续期间不断增加。 失去该表格会使分组失效; 重新启动其全部 silos 以建立新的组合。 一个幸存的 silo 读取了它所知道的成员版本下方的表格版本,引用了 <xref:Orleans.Runtime.IFatalErrorHandler.OnFatalException*>。 重叠的文字是,比较使用每次阅读时已知的版本。

预报通常是最简单的选择,因为它分配端点、开始依赖关系、输入配置和每个复制的显示日志。

> [!IMPORTANT]
> 不要部署本地主机、静态、开发、记忆或模拟器支持的供应商作为生产基础设施。它们不能提供多节部署所预期的持久性和可用性。

<a id="choose-local-backing-services"></a>

## 选择本地支持服务

使用模拟供应商进行快速单位级迭代。 在需要测试供应商行为、序列格式、系统设置或重新启动耐久性时, 使用容器或模拟器。 保持与生产所使用的相同的成套提供者和配置形状。

对于生成流程内集群的集成测试,请使用 [`Microsoft.Orleans.TestingHost`](https://www.nuget.org/packages/Microsoft.Orleans.TestingHost) 包和 <xref:Orleans.TestingHost.TestClusterBuilder> ,而不是手动指定端口。完整应用程序见 [Orleans 样本](https://github.com/dotnet/orleans/tree/main/samples)。
