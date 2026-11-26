---
title: "Orleans 配置指南"
description: "配置 Orleans silos、客户端、提供者和终点。"
ms.date: 08/18/2026
ms.topic: overview
---

<a id="orleans-configuration-guide"></a>

# Orleans 配置指南

Orleans 使用 [.NET 通用主机](https://learn.microsoft.com/dotnet/core/extensions/generic-host)、 [补充性注射](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection)和 [.NET 选项模式](https://learn.microsoft.com/dotnet/core/extensions/options)。

| 型 型 | 使用它当 | 切入点 |
|---|---|---|
| Silo 与共同托管客户端的客户端 | 进程主机 grains , 并同时调用 grains。 对于大多数服务来说, 这是默认值 。 | `builder.UseOrleans(...)` |
| 外部客户 | 单独的进程,如网络前端,呼叫远程 Orleans 集,但不主机 grains。 | `builder.UseOrleansClient(...)` |
| 精密刻写应用程序 | 提醒创建支持资源, 输入 Orleans 配置和服务引用 。 | `builder.AddOrleans(...)` 中在 AppHost,然后无参数 `UseOrleans()` 或 `UseOrleansClient()` |

第一个本地流程见 [地方发展配置](local-development-configuration.md)。生产时,配置 silos 和具有相同集群特性和集群提供商的外部客户:

- [服务器配置](server-configuration.md)
- [客户配置](client-configuration.md)
- [典型配置](typical-configurations.md)
- [Orleans 和指定集成](../aspire-integration.md)

<a id="programmatic-configuration"></a>

## 方案配置

创建 [.NET 通用主机](https://learn.microsoft.com/dotnet/core/extensions/generic-host) , 并使用 <xref:Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder*>, 调用 <xref:Microsoft.Extensions.Hosting.OrleansSiloGenericHostExtensions.UseOrleans*> 添加一个 silo, 然后构建和运行主机 :

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="run_silo":::

启动主机开始 silo 并联合主持 <xref:Orleans.IClusterClient>。客户可以通过依赖注射方式获得,并停止主机坐标为 [优美 Orleans 关闭](shutting-down-orleans.md)。

配置 silo 贯穿于 <xref:Orleans.Hosting.ISiloBuilder> 传递到 `UseOrleans`外部客户流程的使用 <xref:Microsoft.Extensions.Hosting.OrleansClientGenericHostExtensions.UseOrleansClient*> 和配置 <xref:Orleans.Hosting.IClientBuilder> 提供方扩展方法在主机启动时验证配置。程序设置支持以SDK 对象等提供的证书 <xref:Azure.Core.TokenCredential>、计算配置和编译时间 API 发现

汇编实例见 [服务器配置](server-configuration.md) 和 [客户配置](client-configuration.md) 。

<a id="declarative-configuration"></a>

## 声明配置

Orleans 自动绑定 `Orleans` 当 <xref:Microsoft.Extensions.Hosting.OrleansSiloGenericHostExtensions.UseOrleans*> 或 <xref:Microsoft.Extensions.Hosting.OrleansClientGenericHostExtensions.UseOrleansClient*> 即需说明。以下各节被确认:

| 路径路径路径 | 应用 | 目的目的宗旨 |
|---|---|---|
| `Orleans` | Silo 和客户 | <xref:Orleans.Configuration.ClusterOptions>,包括 <xref:Orleans.Configuration.ClusterOptions.ServiceId> 和 <xref:Orleans.Configuration.ClusterOptions.ClusterId> |
| `Orleans:Name` | Silo | Silo 名称 |
| `Orleans:Messaging` | Silo 和客户 | <xref:Orleans.Configuration.SiloMessagingOptions> 或 <xref:Orleans.Configuration.ClientMessagingOptions> |
| `Orleans:Gateway` | 客户客户 | 端口刷新和连接行为 |
| `Orleans:Endpoints` | Silo | 广告和收听端点 |
| `Orleans:Clustering` | Silo 和客户 | 单一集群提供商 |
| `Orleans:Reminders` | Silo | 一家提醒通知提供机构 |
| `Orleans:Journaling:{name}` | Silo | 命名日记存储提供者; `Default` 配置 grain 日记 |
| `Orleans:BroadcastChannel:{name}` | Silo 和客户 | 命名广播频道供应商 |
| `Orleans:Streaming:{name}` | Silo 和客户 | 命名流源源代码提供者 |
| `Orleans:GrainStorage:{name}` | Silo | 命名存储供应商 grain |
| `Orleans:GrainDirectory:{name}` | Silo | 命名 grain 目录提供者 |

提供方区域选择 `ProviderType`注册的供应商。安装提供者的 NuGet 软件包,这样可以发现其配置构建器。请参见提供方目录中的 [服务器配置](server-configuration.md#clustering-provider) 和部署导向示例中的 [典型配置](typical-configurations.md) 。

环境变量使用双下划线,例如 `Orleans__ClusterId` 和 `Orleans__Endpoints__SiloPort`。

> [!NOTE]
> 声明提供方的名称来自安装的供应商组件。如果 Orleans 报告未知 `ProviderType`,应核实相应的软件包被引用,并使用该软件包所记录的供应商名称。

<a id="configuration-precedence"></a>

## 组合位数

通用主机按其正常顺序组合 [.NET 配置供应商](https://learn.microsoft.com/dotnet/core/extensions/configuration-providers) 。程序选项配置也参与选项管道,以避免在多个地方配置相同价值,除非超值是故意的。在部署时保持服务及集成特性稳定并输入特定环境端点、证书和供应商连接细节。

<a id="production-checklist"></a>

## 生产清单

- 使用耐久的、共享的集群提供商;不为生产集群使用开发或静态集群。
- 给每个 silo 和客户以相同的 <xref:Orleans.Configuration.ClusterOptions.ServiceId>、 <xref:Orleans.Configuration.ClusterOptions.ClusterId>和组群提供者设置。
- 广告地址可供其他 silos 和客户(特别是NAT、集装箱或负载平衡器后面的用户)获取。
- 在应用取决于这些特性时,使用耐久提醒和 grain 存储提供者。
- 审评 [Orleans 安全责任和信任边界](../../security/index.md)。
- 配置 [服务器垃圾收集](configuring-garbage-collection.md)。
- 允许通用主机执行 [宽限期关闭](shutting-down-orleans.md)。
- 在推出之前验证供应商的连通性和证书。

选项类型和 API 入境点见 [核心配置备选方案](list-of-options-classes.md) 和 <xref:Orleans.Configuration>。

任务导向配置配方见 [Orleans 如何指导索引](../../how-to/index.md)。配置 API 的确切签名和默认值请使用 [C# API 参考](https://dotnet.github.io/orleans/docs/api/csharp/)。
