---
title: "Orleans 和指定集成"
description: "与 Aspire 模拟并运行 Orleans 应用程序。"
ms.date: 08/10/2026
ms.topic: how-to
---

<a id="orleans-and-aspire-integration"></a>

# Orleans 和指定集成

<a id="overview"></a>

## 概述

<a id="prerequisites"></a>

`Aspire.Hosting.Orleans` 套件模式是一个 Orleans 组及其在一个“接收器” AppHost中的辅助服务。 [指定](https://aspire.dev/get-started/what-is-aspire/) 供应集群特性、端点、供应商配置、服务发现、依赖性订购和对 silo 项目和客户项目的可观察性环境。

使用 高级 , 您需要可重复本地分布环境时使用 。 请使用 AppHost 来描述部署资源 。 指定 管弦 Orleans; 它不会替换 Orleans 组、 存储、 提醒或流源提供商 。 支持的工具链请参见 [安装天使 CLI](https://aspire.dev/get-started/install-cli/) 。

<a id="configure-the-apphost"></a>

## 配置 AppHost

<a id="required-packages"></a>
<a id="apphost-project"></a>

参考 `Aspire.Hosting.Orleans` 和您所用资源的提醒整合:

:::code language="xml" source="snippets/aspire/AppHost/AppHost.csproj" id="apphost_packages":::

定义组合资源,并 Orleans 资源, 然后引用 Orleans 从 silo 项目 :

<a id="basic-orleans-cluster-with-redis-clustering"></a>

:::code language="csharp" source="snippets/aspire/AppHost/AppHostExamples.cs" id="basic_orleans_cluster":::

`.WithReplicas(3)` 开始三个本地 silo 复制。 `.WaitFor(redis)` 阻止 silo 项目在 Redis 准备就绪之前启动。

仅添加应用需要的能力:

<a id="orleans-with-grain-storage-and-reminders"></a>

:::code language="csharp" source="snippets/aspire/AppHost/AppHostExamples.cs" id="orleans_with_storage_reminders":::

名称为 grain 储存资源对应的名称是 Orleans 供应商,如 `Default` 和 `PubSubStore`。

<a id="configure-the-orleans-silo-project"></a>

## 配置 Orleans silo 项目

<a id="orleans-silo-project"></a>
<a id="service-defaults-pattern"></a>

注册 Orleans消耗的每一种支持资源, 然后调用无参数 <xref:Microsoft.Extensions.Hosting.OrleansSiloGenericHostExtensions.UseOrleans*>的有键化的 Aspire 客户端:

:::code language="csharp" source="snippets/aspire/Silo/SiloProgram.cs" id="silo_basic_config":::

AppHost 输入 `Orleans` 配置等级。 Orleans 将集特性、端点、组合、提醒、流、 grain 存储和 grain 目录配置绑在一起。

> [!IMPORTANT]
> 资源引用输入配置, 但应用程序项目必须注册匹配的密钥服务客户端。 例如, 请使用 `AddKeyedRedisClient`、 `AddKeyedAzureTableServiceClient`或匹配的资源类型和名称的 Aspire 集成法 。

仅使用 <xref:Microsoft.Extensions.Hosting.OrleansSiloGenericHostExtensions.UseOrleans*> 代表配置,而 AppHost 不进行模型配置,例如具体应用程序选项或定制服务。

<a id="configure-the-orleans-client-project"></a>

## 配置 Orleans 客户端工程

<a id="orleans-client-project-if-separate-from-silo"></a>

<a id="separate-silo-and-client-projects"></a>

创建 Orleans 资源仅客户端视图, 使用 `.AsClient()`:

:::code language="csharp" source="snippets/aspire/AppHost/AppHostExamples.cs" id="separate_silo_and_client":::

在客户项目中,登记按键资源客户端并拨打无参数 <xref:Microsoft.Extensions.Hosting.OrleansClientGenericHostExtensions.UseOrleansClient*>:

:::code language="csharp" source="snippets/aspire/Client/ClientProgram.cs" id="client_basic_config":::

客户与 silos一样,获得相同的集群身份和集群提供商设置,但没有收到 silo 托管能力。

<a id="azure-storage-with-aspire"></a>

## Azure 与寄存存储

<a id="development-vs-production-configuration"></a>
<a id="local-development-using-emulators"></a>
<a id="production-using-managed-services"></a>

本汇编的例子使用 Azurite 进行本地 Azure 存储开发:

:::code language="csharp" source="snippets/aspire/AppHost/AppHostExamples.cs" id="azure_storage_aspire":::

注册匹配 Azure 表格客户 silo:

:::code language="csharp" source="snippets/aspire/Silo/SiloProgram.cs" id="silo_azure_config":::

`.RunAsEmulator()` 是一个本地发展选择。对于生产, Azure 存储资源与真实账户绑在一起,并在部署环境中配置身份和访问权限。不要将模拟器配置复制为生产 AppHost。

同一原则也适用于 Redis 和数据库: AppHost 资源在开发期间可以发射一个当地集装箱,并被装设中受管理的服务所约束。

<a id="provider-wiring-reference"></a>

## 提供方电线参考

自动配置外部资源时, AppHost 需要相应的 Aspire 主机整合。 silo 或客户端既需要 Orleans 提供方软件包,也需要 Aspire 客户端整合,并且它必须使用 Aspire 资源名登记客户端。

| 资源资源资源资源资源资源资源资源 | 申请登记 | 支持的自动 Orleans 配置 |
|---|---|---|
| Redis | `AddKeyedRedisClient` | 集束、 grain 存储、提醒和 grain 目录 |
| Azure 表格 | `AddKeyedAzureTableServiceClient` | 集束、 grain 存储、提醒和 grain 目录 |
| Azure 组合 | `AddKeyedAzureBlobServiceClient` | Grain 储存 |
| ADO.NET 数据库 | 配置来自注入连接字符串的 Orleans 提供者 | 集群、 grain 储存和提醒通知需要手工配置 |
| In-memory | 无无无无 | 发展集群、 grain 储存、提醒和流 |

由此形成的提供者支助矩阵是:

| 能力 | Redis | Azure 表格 | Azure 组合 | ADO.NET | In-memory |
|---|---|---|---|---|---|
| 集群组合 | 自动自动 | 自动自动 | 无 | 《手册手册手册手册》 | 仅开发 |
| Grain 储存 | 自动自动 | 自动自动 | 自动自动 | 《手册手册手册手册》 | 仅开发 |
| 提醒提醒 | 自动自动 | 自动自动 | 无 | 《手册手册手册手册》 | 仅开发 |
| Grain 目录 | 自动自动 | 自动自动 | 无 | 无 | 无 |

<a id="adonet-providers"></a>

### ADO.NET 提供者

ADO.NET 资源类型不推断 `AdoNet` 需要的 服务名 Orleans,和 `Aspire.Hosting.Orleans` 并不暴露 API 直接引用数据库资源,从 silo 工程,使 Aspire 输入其连接字符串:

:::code language="csharp" source="snippets/aspire/AppHost/AppHostExamples.cs" id="adonet_apphost":::

配置该连接字符串中的 Orleans ADO.NET 提供者:

:::code language="csharp" source="snippets/aspire/Silo/SiloProgram.cs" id="adonet_silo":::

仅在应用程序代码也消耗该数据库客户端时,才单独注册 Aspire 数据库客户端。 Orleans ADO.NET 提供者直接使用他们配置的连接字符串。

<a id="grain-directories"></a>

### Grain 目录

Redis 和 Azure 表格可以回命名 grain 目录。此示例配置 Redis grain 目录中的 AppHost:

:::code language="csharp" source="snippets/aspire/AppHost/AppHostExamples.cs" id="grain_directory_apphost":::

登记册登记登记册 Redis 使用同一资源名的客户端 silo:

:::code language="csharp" source="snippets/aspire/Silo/SiloProgram.cs" id="grain_directory_silo":::

<a id="apphost-extension-methods-reference"></a>

## AppHost 推广方法参考

`AddOrleans` 生成标准 Orleans 配置。应用程序项目仍然呼叫 <xref:Microsoft.Extensions.Hosting.OrleansSiloGenericHostExtensions.UseOrleans*> 或 <xref:Microsoft.Extensions.Hosting.OrleansClientGenericHostExtensions.UseOrleansClient*>,并且 Orleans 在启动时验证由此形成的供应商配置。在诊断缺失的提供者、键盘资源或终点时,您可以在星盘上检查注入的环境变量。

共同 AppHost 业务包括:

<a id="core-methods"></a>
<a id="clustering"></a>
<a id="grain-storage"></a>
<a id="reminders"></a>
<a id="streaming"></a>
<a id="grain-directory"></a>

| 业务 | 目的目的宗旨 |
|---|---|
| `AddOrleans(name)` | 定义 Orleans 群集资源。 |
| `WithClustering(resource)` | 选择成员和网关提供者。 |
| `WithGrainStorage(name, resource)` | 添加名为 grain 的存储 。 |
| `WithReminders(resource)` | 增加一个耐久提醒提供者。 |
| `WithStreaming(name, resource)` | 添加一个命名的流提供商 。 |
| `WithGrainDirectory(name, resource)` | 添加名为 grain 目录 。 |
| `AsClient()` | 引用仅客户端项目的群集。 |
| `WithReference(orleans)` | 向工程输入 Orleans 配置。 |

查看您 Aspire 版本支持的资源类型和超载的 [指定 Orleans 集成引用](https://aspire.dev/integrations/frameworks/orleans/) 。

<a id="best-practices"></a>

## 最佳做法

<a id="opentelemetry-configuration"></a>
<a id="health-checks"></a>
<a id="see-also"></a>

设置稳定的服务和集束识别器,用于环境,在重新启动和滚动部署期间必须相互连接:

:::code language="csharp" source="snippets/aspire/AppHost/AppHostExamples.cs" id="explicit_cluster_ids":::

- AppHost 作为资源模式,而不是取代持久服务。
- 使用管理下的身份或工作量身份,而不是隐藏秘密。
- 保持 <xref:Orleans.Configuration.ClusterOptions.ServiceId> 环境的稳定性和隔离性 <xref:Orleans.Configuration.ClusterOptions.ClusterId>。
- 运行多个 silo 重复, 并运行多个失败域的复制。
- 每个应用项目都配置就绪、遥测输出和优雅终止。
- 将关键服务名称完全匹配在 AppHost 项目与应用项目之间。
