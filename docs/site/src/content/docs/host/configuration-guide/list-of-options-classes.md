---
title: "核心 Orleans 配置选项"
description: "查找 Orleans 用于共同主机任务选项类型。"
ms.date: 08/02/2026
ms.topic: reference
---

<a id="core-orleans-configuration-options"></a>

# 核心 Orleans 配置选项

Orleans 选项使用 [.NET 选项模式](https://learn.microsoft.com/dotnet/core/extensions/options)。配置它们时使用 <xref:Orleans.Hosting.SiloBuilderExtensions.Configure*> 或 <xref:Orleans.Hosting.ClientBuilderExtensions.Configure*>。 Orleans 也自动约束 [声明配置](index.md#declarative-configuration)所列的具体部分;其他选项类型需要明确的约束性。

本页整理了常用配置选项，供入门参考。完整的选项类型和属性以当前安装版本的 <xref:Orleans.Configuration> 及各提供程序的 API 参考为准。

<a id="common-core-options-for-client-and-silo-builders"></a>

## 客户和 silo 构建器的通用核心选项

| 选项类型 | 用来 |
|---|---|
| <xref:Orleans.Configuration.ClusterOptions> | <xref:Orleans.Configuration.ClusterOptions.ServiceId> 和 <xref:Orleans.Configuration.ClusterOptions.ClusterId> 共有 silos 客户和客户 |

<a id="iclientbuilder-specific-options"></a>

<a id="-specific-options"></a>

## <xref:Orleans.Hosting.IClientBuilder>- 特定备选办法

| 选项类型 | 用来 |
|---|---|
| <xref:Orleans.Configuration.ClientMessagingOptions> | 外部客户信息传递和联系 |
| <xref:Orleans.Configuration.GatewayOptions> | 客户端网关更新和首选网关行为 |

<a id="isilobuilder-specific-options"></a>

<a id="-specific-options-1"></a>

## <xref:Orleans.Hosting.ISiloBuilder>- 特定备选办法

| 选项类型 | 用来 |
|---|---|
| <xref:Orleans.Configuration.SiloMessagingOptions> | Silo 信息、响应超时和连接行为 |
| <xref:Orleans.Configuration.EndpointOptions> | 广告 silo/门口和收听端点 |
| <xref:Orleans.Configuration.SiloOptions> | Silo 名称 |
| <xref:Orleans.Configuration.ClusterMembershipOptions> | 成员测试、故障检测和初步连接验证 |
| <xref:Orleans.Configuration.GrainCollectionOptions> | 空闲激活收集与内存压力悬停 |
| <xref:Orleans.Configuration.GrainDirectoryOptions> | 内置 grain 目录缓存和分区行为 |
| <xref:Orleans.Configuration.LoadSheddingOptions> | 请求在主负载下被拒绝 |
| <xref:Orleans.Configuration.SchedulingOptions> | Grain 排期限制和诊断 |
| <xref:Orleans.Configuration.GrainTypeOptions> |  进程支持的 Grain 类和接口 |

<a id="feature-specific-options"></a>

## 具体特地选项

存储、集束、提醒、流体、序列、仪表板和第三方整合等定义了自己包件中的选项。从供应商的构建器方法(如 <xref:Microsoft.Extensions.Hosting.RedisClusteringISiloBuilderExtensions.UseRedisClustering*>、 <xref:Orleans.Hosting.AzureBlobSiloBuilderExtensions.AddAzureBlobGrainStorage*>或 <xref:Orleans.Hosting.SiloBuilderReminderExtensions.UseAdoNetReminderService*>)开始,然后在参考 IntelliSense 或 API 中遵循链接选项类型。

命名的提供者通常使用其构建器方法配置配置 :

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="named_providers":::

使用声明命名的供应商 `Orleans:{capability}:{name}` 和 a 以及 `ProviderType`; 见 [声明配置](index.md#declarative-configuration).

<a id="find-an-option"></a>

## 查找选项

1. 从该特性的主机扩展方法开始 。
2. 遵循 IntelliSense中代表类型选项。
3. 检查安装的软件包 API 引用, 以确认默认值和验证值。
4. 检查启动验证错误; Orleans 在 silo 或客户端准备就绪之前对所需供应商设置进行验证。

避免将所有可用属性复制为配置。 除非部署要求或测量证明需要超文本, 否则保留默认值。 这样可以减少版本漂移并让有意调试可见 。
