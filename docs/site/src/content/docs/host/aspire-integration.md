---
title: Orleans 与 Aspire 集成
description: 使用 Aspire 对 Orleans 应用程序建模并运行应用程序。
ms.date: 08/10/2026
ms.topic: how-to
---

<a id="orleans-and-aspire-integration"></a>

# Orleans 与 Aspire 集成

<a id="overview"></a>

## 概述

<a id="prerequisites"></a>

`Aspire.Hosting.Orleans` 包在 Aspire AppHost 中对 Orleans 集群及其后端服务进行建模。[Aspire](https://aspire.dev/get-started/what-is-aspire/) 为 Silo 和客户端项目提供集群标识、端点、提供程序配置、服务发现、依赖项启动顺序及可观测性上下文。

如果需要可重复创建的本地分布式环境，或者已经使用 AppHost 描述部署资源，请使用 Aspire。Aspire 编排 Orleans，但不会替代 Orleans 集群、存储、提醒器或流提供程序。有关受支持的工具链，请参阅 [安装 Aspire CLI](https://aspire.dev/get-started/install-cli/)。

<a id="configure-the-apphost"></a>

## 配置 AppHost

<a id="required-packages"></a>
<a id="apphost-project"></a>

引用 `Aspire.Hosting.Orleans` 以及所用资源对应的 Aspire 集成包：

:::code language="xml" source="snippets/aspire/AppHost/AppHost.csproj" id="apphost_packages":::

定义集群成员资格资源和 Orleans 资源，然后从 Silo 项目引用 Orleans：

<a id="basic-orleans-cluster-with-redis-clustering"></a>

:::code language="csharp" source="snippets/aspire/AppHost/AppHostExamples.cs" id="basic_orleans_cluster":::

`.WithReplicas(3)` 启动三个本地 Silo 副本。`.WaitFor(redis)` 阻止 Silo 项目在 Redis 就绪之前启动。

仅添加应用程序所需的功能：

<a id="orleans-with-grain-storage-and-reminders"></a>

:::code language="csharp" source="snippets/aspire/AppHost/AppHostExamples.cs" id="orleans_with_storage_reminders":::

命名的 Grain 存储资源对应于 `Default` 和 `PubSubStore` 等命名的 Orleans 提供程序。

<a id="configure-the-orleans-silo-project"></a>

## 配置 Orleans Silo 项目

<a id="orleans-silo-project"></a>
<a id="service-defaults-pattern"></a>

为 Orleans 使用的每项后端资源注册带键的 Aspire 客户端，然后调用无参数的 <xref:Microsoft.Extensions.Hosting.OrleansSiloGenericHostExtensions.UseOrleans*>：

:::code language="csharp" source="snippets/aspire/Silo/SiloProgram.cs" id="silo_basic_config":::

AppHost 注入 `Orleans` 配置层次结构。Orleans 从中绑定集群标识、端点、集群成员资格、提醒器、流、Grain 存储及 Grain 目录配置。

> [!IMPORTANT]
> 资源引用会注入配置，但应用程序项目必须注册匹配的带键服务客户端。例如，根据资源类型和名称，使用 `AddKeyedRedisClient`、`AddKeyedAzureTableServiceClient` 或相匹配的 Aspire 集成方法。

仅对 AppHost 未建模的配置使用 <xref:Microsoft.Extensions.Hosting.OrleansSiloGenericHostExtensions.UseOrleans*> 委托，例如应用程序特定选项或自定义服务。

<a id="configure-the-orleans-client-project"></a>

## 配置 Orleans 客户端项目

<a id="orleans-client-project-if-separate-from-silo"></a>

<a id="separate-silo-and-client-projects"></a>

使用 `.AsClient()` 创建 Orleans 资源的仅客户端视图：

:::code language="csharp" source="snippets/aspire/AppHost/AppHostExamples.cs" id="separate_silo_and_client":::

在客户端项目中，注册带键的资源客户端并调用无参数的 <xref:Microsoft.Extensions.Hosting.OrleansClientGenericHostExtensions.UseOrleansClient*>：

:::code language="csharp" source="snippets/aspire/Client/ClientProgram.cs" id="client_basic_config":::

客户端接收与 Silo 相同的集群标识及集群提供程序设置，但不会获得 Silo 托管能力。

<a id="azure-storage-with-aspire"></a>

## 将 Azure 存储与 Aspire 配合使用

<a id="development-vs-production-configuration"></a>
<a id="local-development-using-emulators"></a>
<a id="production-using-managed-services"></a>

此可编译示例使用 Azurite 进行本地 Azure 存储开发：

:::code language="csharp" source="snippets/aspire/AppHost/AppHostExamples.cs" id="azure_storage_aspire":::

在 Silo 中注册匹配的 Azure 表客户端：

:::code language="csharp" source="snippets/aspire/Silo/SiloProgram.cs" id="silo_azure_config":::

`.RunAsEmulator()` 是用于本地开发的选择。在生产环境中，将 Azure 存储资源绑定到真实帐户，并在部署环境中配置标识和访问权限。不要将模拟器配置复制到生产 AppHost 中。

相同原则也适用于 Redis 和数据库：AppHost 资源可在开发阶段启动本地容器，在部署时绑定托管服务。

<a id="provider-wiring-reference"></a>

## 提供程序连接配置参考

对于自动配置的外部资源，AppHost 需要相应的 Aspire 托管集成。Silo 或客户端既需要 Orleans 提供程序包，也需要 Aspire 客户端集成包，并且必须使用 Aspire 资源名称注册客户端。

| 资源 | 应用程序注册 | 支持的 Orleans 自动配置 |
|---|---|---|
| Redis | `AddKeyedRedisClient` | 集群成员资格、Grain 存储、提醒器及 Grain 目录 |
| Azure 表 | `AddKeyedAzureTableServiceClient` | 集群成员资格、Grain 存储、提醒器及 Grain 目录 |
| Azure Blob | `AddKeyedAzureBlobServiceClient` | Grain 存储 |
| ADO.NET 数据库 | 通过注入的连接字符串配置 Orleans 提供程序 | 集群成员资格、Grain 存储及提醒器需要手动配置 |
| 内存 | 无 | 开发环境的集群成员资格、Grain 存储、提醒器及流 |

由此得到的提供程序支持矩阵如下：

| 功能 | Redis | Azure 表 | Azure Blob | ADO.NET | 内存 |
|---|---|---|---|---|---|
| 集群成员资格 | 自动 | 自动 | 否 | 手动 | 仅开发环境 |
| Grain 存储 | 自动 | 自动 | 自动 | 手动 | 仅开发环境 |
| 提醒器 | 自动 | 自动 | 否 | 手动 | 仅开发环境 |
| Grain 目录 | 自动 | 自动 | 否 | 否 | 否 |

<a id="adonet-providers"></a>

### ADO.NET 提供程序

ADO.NET 资源类型不会推断出 Orleans 所需的 `AdoNet` 提供程序名称，并且 `Aspire.Hosting.Orleans` 未公开用于覆盖推断名称的 API。直接从 Silo 项目引用数据库资源，使 Aspire 注入其连接字符串：

:::code language="csharp" source="snippets/aspire/AppHost/AppHostExamples.cs" id="adonet_apphost":::

通过该连接字符串配置 Orleans ADO.NET 提供程序：

:::code language="csharp" source="snippets/aspire/Silo/SiloProgram.cs" id="adonet_silo":::

仅当应用程序代码也使用该数据库客户端时，才单独注册 Aspire 数据库客户端。Orleans ADO.NET 提供程序直接使用其配置的连接字符串。

<a id="grain-directories"></a>

### Grain 目录

Redis 和 Azure 表可作为命名 Grain 目录的后端。以下示例在 AppHost 中配置 Redis Grain 目录：

:::code language="csharp" source="snippets/aspire/AppHost/AppHostExamples.cs" id="grain_directory_apphost":::

在 Silo 中以相同资源名称注册 Redis 客户端：

:::code language="csharp" source="snippets/aspire/Silo/SiloProgram.cs" id="grain_directory_silo":::

<a id="apphost-extension-methods-reference"></a>

## AppHost 扩展方法参考

`AddOrleans` 生成标准 Orleans 配置。应用程序项目仍需调用 <xref:Microsoft.Extensions.Hosting.OrleansSiloGenericHostExtensions.UseOrleans*> 或 <xref:Microsoft.Extensions.Hosting.OrleansClientGenericHostExtensions.UseOrleansClient*>，Orleans 会在启动时验证得到的提供程序配置。在排查缺失的提供程序、带键资源或端点时，可在 Aspire 仪表板中检查注入的环境变量。

常用 AppHost 操作包括：

<a id="core-methods"></a>
<a id="clustering"></a>
<a id="grain-storage"></a>
<a id="reminders"></a>
<a id="streaming"></a>
<a id="grain-directory"></a>

| 操作 | 用途 |
|---|---|
| `AddOrleans(name)` | 定义 Orleans 集群资源。 |
| `WithClustering(resource)` | 选择成员资格和网关提供程序。 |
| `WithGrainStorage(name, resource)` | 添加命名 Grain 存储。 |
| `WithReminders(resource)` | 添加持久化提醒器提供程序。 |
| `WithStreaming(name, resource)` | 添加命名流提供程序。 |
| `WithGrainDirectory(name, resource)` | 添加命名 Grain 目录。 |
| `AsClient()` | 从仅客户端项目引用集群。 |
| `WithReference(orleans)` | 向项目注入 Orleans 配置。 |

有关你的 Aspire 版本支持的资源类型及重载，请查阅 [Aspire Orleans 集成参考](https://aspire.dev/integrations/frameworks/orleans/)。

<a id="best-practices"></a>

## 最佳实践

<a id="opentelemetry-configuration"></a>
<a id="health-checks"></a>
<a id="see-also"></a>

对于必须在重启和滚动部署期间保持互操作的环境，设置稳定的服务和集群标识符：

:::code language="csharp" source="snippets/aspire/AppHost/AppHostExamples.cs" id="explicit_cluster_ids":::

- 将 AppHost 视为资源模型，而非持久化服务的替代品。
- 使用托管标识或工作负载标识，不要嵌入机密。
- 保持 <xref:Orleans.Configuration.ClusterOptions.ServiceId> 稳定，并使用 <xref:Orleans.Configuration.ClusterOptions.ClusterId> 隔离环境。
- 跨故障域运行多个 Silo 副本。
- 在各应用程序项目中配置就绪检查、遥测导出及优雅终止。
- AppHost 与应用程序项目之间的带键服务名称必须完全一致。
