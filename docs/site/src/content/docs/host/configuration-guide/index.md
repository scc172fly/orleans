---
title: Orleans 配置指南
description: 配置 Orleans Silo、客户端、提供程序及终结点。
ms.date: 08/18/2026
ms.topic: overview
---

<a id="orleans-configuration-guide"></a>

# Orleans 配置指南

Orleans 使用 [.NET 通用宿主](https://learn.microsoft.com/dotnet/core/extensions/generic-host)、[依赖注入](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection)及 [.NET 选项模式](https://learn.microsoft.com/dotnet/core/extensions/options)。从以下一种托管模型开始：

| 模型 | 适用场景 | 入口点 |
|---|---|---|
| 共同托管客户端的 Silo | 进程既托管 Grain，又调用 Grain。这是大多数服务的默认模型。 | `builder.UseOrleans(...)` |
| 外部客户端 | 独立进程（例如 Web 前端）调用远程 Orleans 集群，但不托管 Grain。 | `builder.UseOrleansClient(...)` |
| Aspire 编排的应用 | Aspire 创建后端资源，并注入 Orleans 配置及服务引用。 | 在 AppHost 中调用 `builder.AddOrleans(...)`，然后调用无参数的 `UseOrleans()` 或 `UseOrleansClient()` |

对于第一个本地进程，参阅[本地开发配置](local-development-configuration.md)。对于生产环境，使用相同的集群标识及集群成员资格提供程序配置 Silo 与外部客户端：

- [服务端配置](server-configuration.md)
- [客户端配置](client-configuration.md)
- [典型配置](typical-configurations.md)
- [Orleans 与 Aspire 集成](../aspire-integration.md)

<a id="programmatic-configuration"></a>

## 编程式配置

使用 <xref:Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder*> 创建 [.NET 通用宿主](https://learn.microsoft.com/dotnet/core/extensions/generic-host)，调用 <xref:Microsoft.Extensions.Hosting.OrleansSiloGenericHostExtensions.UseOrleans*> 添加 Silo，然后构建并运行宿主：

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="run_silo":::

启动宿主会启动 Silo 及其共同托管的 <xref:Orleans.IClusterClient>。可以通过依赖注入获取客户端；停止宿主时会协调 [Orleans 的优雅关闭](shutting-down-orleans.md)。

通过传给 `UseOrleans` 的 <xref:Orleans.Hosting.ISiloBuilder> 配置 Silo。外部客户端进程使用 <xref:Microsoft.Extensions.Hosting.OrleansClientGenericHostExtensions.UseOrleansClient*>，并配置传给它的 <xref:Orleans.Hosting.IClientBuilder>。提供程序扩展方法在宿主启动时验证配置。编程式配置支持以 SDK 对象（例如 <xref:Azure.Core.TokenCredential>）提供凭据、使用计算得到的配置，以及在编译期发现 API。

有关经过编译检查的示例，参阅[服务端配置](server-configuration.md)及[客户端配置](client-configuration.md)。

<a id="declarative-configuration"></a>

## 声明式配置

调用 <xref:Microsoft.Extensions.Hosting.OrleansSiloGenericHostExtensions.UseOrleans*> 或 <xref:Microsoft.Extensions.Hosting.OrleansClientGenericHostExtensions.UseOrleansClient*> 时，Orleans 会自动绑定 `Orleans` 配置节。以下配置节会被识别：

| 路径 | 适用对象 | 用途 |
|---|---|---|
| `Orleans` | Silo 和客户端 | <xref:Orleans.Configuration.ClusterOptions>，包括 <xref:Orleans.Configuration.ClusterOptions.ServiceId> 及 <xref:Orleans.Configuration.ClusterOptions.ClusterId> |
| `Orleans:Name` | Silo | Silo 名称 |
| `Orleans:Messaging` | Silo 和客户端 | <xref:Orleans.Configuration.SiloMessagingOptions> 或 <xref:Orleans.Configuration.ClientMessagingOptions> |
| `Orleans:Gateway` | 客户端 | 网关刷新及连接行为 |
| `Orleans:Endpoints` | Silo | 通告及监听终结点 |
| `Orleans:Clustering` | Silo 和客户端 | 单个集群成员资格提供程序 |
| `Orleans:Reminders` | Silo | 单个提醒器提供程序 |
| `Orleans:Journaling:{name}` | Silo | 命名日志存储提供程序；`Default` 配置 Grain 日志化状态 |
| `Orleans:BroadcastChannel:{name}` | Silo 和客户端 | 命名广播通道提供程序 |
| `Orleans:Streaming:{name}` | Silo 和客户端 | 命名流提供程序 |
| `Orleans:GrainStorage:{name}` | Silo | 命名 Grain 存储提供程序 |
| `Orleans:GrainDirectory:{name}` | Silo | 命名 Grain 目录提供程序 |

提供程序配置节通过 `ProviderType` 选择已注册的提供程序。安装提供程序的 NuGet 包，使其配置构建器可被发现。有关提供程序目录，参阅[服务端配置](server-configuration.md#clustering-provider)；有关面向部署的示例，参阅[典型配置](typical-configurations.md)。

环境变量使用双下划线，例如 `Orleans__ClusterId` 及 `Orleans__Endpoints__SiloPort`。

> [!NOTE]
> 声明式提供程序名称来自已安装的提供程序程序集。如果 Orleans 报告未知的 `ProviderType`，确认已引用相应包，并使用该包文档中指定的提供程序名称。

<a id="configuration-precedence"></a>

## 配置优先级

通用宿主按常规顺序组合 [.NET 配置提供程序](https://learn.microsoft.com/dotnet/core/extensions/configuration-providers)。编程式选项配置也参与选项管道，因此除非有意覆盖，否则不要在多个位置配置同一个值。保持服务及集群标识稳定，并在部署时注入环境专用终结点、凭据及提供程序连接详情。

<a id="production-checklist"></a>

## 生产环境检查清单

- 使用持久化的共享集群成员资格提供程序；生产集群不要使用开发或静态集群配置。
- 为每个 Silo 和客户端设置相同的 <xref:Orleans.Configuration.ClusterOptions.ServiceId>、<xref:Orleans.Configuration.ClusterOptions.ClusterId> 及集群成员资格提供程序配置。
- 通告其他 Silo 和客户端能够访问的地址，尤其是在 NAT、容器或负载均衡器后方。
- 当应用依赖提醒器及 Grain 存储功能时，使用持久化的提供程序。
- 查阅 [Orleans 安全责任与信任边界](../../security/index.md)。
- 配置[服务器垃圾回收](configuring-garbage-collection.md)。
- 允许通用宿主执行[优雅关闭](shutting-down-orleans.md)。
- 在滚动发布之前验证提供程序连通性及凭据。

有关选项类型及 API 入口点，参阅[核心配置选项](list-of-options-classes.md)及 <xref:Orleans.Configuration>。

有关面向任务的配置方案，参阅 [Orleans 操作指南索引](../../how-to/index.md)。有关配置 API 的确切签名及默认值，使用 [C# API 参考](https://dotnet.github.io/orleans/docs/api/csharp/)。
