---
title: Orleans 核心配置选项
description: 查找常见托管任务使用的 Orleans 选项类型。
ms.date: 08/02/2026
ms.topic: reference
---

<a id="core-orleans-configuration-options"></a>

# Orleans 核心配置选项

Orleans 选项使用 [.NET 选项模式](https://learn.microsoft.com/dotnet/core/extensions/options)。使用 <xref:Orleans.Hosting.SiloBuilderExtensions.Configure*> 或 <xref:Orleans.Hosting.ClientBuilderExtensions.Configure*> 进行配置。Orleans 还会自动绑定[声明式配置](index.md#declarative-configuration)中列出的特定配置节；其他选项类型需要显式绑定。

本页是精选的入门参考，并非穷尽所有属性的目录。<xref:Orleans.Configuration> 及提供程序包的 API 才是已安装 Orleans 版本的权威依据。

<a id="common-core-options-for-client-and-silo-builders"></a>

## 客户端和 Silo 构建器通用的核心选项

| 选项类型 | 用途 |
|---|---|
| <xref:Orleans.Configuration.ClusterOptions> | Silo 和客户端共享的 <xref:Orleans.Configuration.ClusterOptions.ServiceId> 及 <xref:Orleans.Configuration.ClusterOptions.ClusterId> |

<a id="iclientbuilder-specific-options"></a>

<a id="-specific-options"></a>

## <xref:Orleans.Hosting.IClientBuilder> 专用选项

| 选项类型 | 用途 |
|---|---|
| <xref:Orleans.Configuration.ClientMessagingOptions> | 外部客户端消息传递及连接 |
| <xref:Orleans.Configuration.GatewayOptions> | 客户端网关刷新及首选网关行为 |

<a id="isilobuilder-specific-options"></a>

<a id="-specific-options-1"></a>

## <xref:Orleans.Hosting.ISiloBuilder> 专用选项

| 选项类型 | 用途 |
|---|---|
| <xref:Orleans.Configuration.SiloMessagingOptions> | Silo 消息传递、响应超时及连接行为 |
| <xref:Orleans.Configuration.EndpointOptions> | 通告的 Silo/网关端口及监听终结点 |
| <xref:Orleans.Configuration.SiloOptions> | Silo 名称 |
| <xref:Orleans.Configuration.ClusterMembershipOptions> | 成员资格探测、故障检测及初始连通性验证 |
| <xref:Orleans.Configuration.GrainCollectionOptions> | 空闲激活实例回收及内存压力下的激活实例削减 |
| <xref:Orleans.Configuration.GrainDirectoryOptions> | 内置 Grain 目录缓存及分区行为 |
| <xref:Orleans.Configuration.LoadSheddingOptions> | 宿主负载过高时拒绝请求 |
| <xref:Orleans.Configuration.SchedulingOptions> | Grain 调度限制及诊断 |
| <xref:Orleans.Configuration.GrainTypeOptions> | 进程支持的 Grain 类及接口 |

<a id="feature-specific-options"></a>

## 特定功能的选项

存储、集群成员资格、提醒器、流式处理、序列化、仪表板及第三方集成在各自的包中定义选项。从提供程序构建器方法入手，例如 <xref:Microsoft.Extensions.Hosting.RedisClusteringISiloBuilderExtensions.UseRedisClustering*>、<xref:Orleans.Hosting.AzureBlobSiloBuilderExtensions.AddAzureBlobGrainStorage*> 或 <xref:Orleans.Hosting.SiloBuilderReminderExtensions.UseAdoNetReminderService*>，然后在 IntelliSense 或 API 参考中查看关联的选项类型。

命名提供程序通常通过其构建器方法配置：

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="named_providers":::

声明式命名提供程序使用 `Orleans:{capability}:{name}` 及 `ProviderType`；参阅[声明式配置](index.md#declarative-configuration)。

<a id="find-an-option"></a>

## 查找选项

1. 从相应功能的托管扩展方法入手。
2. 在 IntelliSense 中查看其选项委托类型。
3. 查阅已安装包的 API 参考，了解默认值及验证规则。
4. 检查启动验证错误；Orleans 会在 Silo 或客户端就绪前验证必需的提供程序设置。

避免将所有可用属性都复制到配置中。除非部署需求或度量结果表明应当覆盖默认值，否则保持默认配置。这可以减少版本漂移，并让有意进行的调优清晰可见。
