---
title: 将 Orleans 应用程序部署到 Azure 容器应用
description: 在 Azure 容器应用上运行、检查、部署并验证多进程 Orleans 应用程序。
ms.date: 08/19/2026
ms.topic: tutorial
---

<a id="deploy-an-orleans-application-to-azure-container-apps"></a>

# 将 Orleans 应用程序部署到 Azure 容器应用

本教程使用 [Azure 容器应用示例](https://github.com/dotnet/orleans/tree/main/samples/Deployment/AzureContainerApps)，引导你从空目录开始，最终部署一个可观测的 Orleans 集群。应用程序、基础设施和部署工作流统一进行版本管理和验证。

完成后的系统包含专用 Silo、HTTP API 和后台工作客户端、Azure 表存储集群成员资格提供程序、托管标识、健康探针、Application Insights 和 Orleans 仪表板。此示例演示集群和部署；若要持久化应用程序状态，还需添加 Grain 存储提供程序。

<a id="prerequisites"></a>

## 先决条件

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Git](https://git-scm.com/downloads)
- 用于本地集群的 [Azurite](https://learn.microsoft.com/azure/storage/common/storage-use-azurite)
- 用于部署的 Azure 订阅和 [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli)

<a id="get-the-application"></a>

## 获取应用程序

从空工作目录开始：

```powershell
git clone https://github.com/dotnet/orleans.git
cd orleans
dotnet build .\samples\Deployment\AzureContainerApps\HelloOrleans.sln -c Release
```

运行项目之前，先了解各项目：

| 项目 | 职责 |
| --- | --- |
| `Abstractions` | 调用方和 Silo 共享的 Grain 接口与序列化契约。 |
| `Grains` | Grain 实现及放置策略。 |
| `Silo` | 专用 Orleans 服务器进程。 |
| `Clients.MinimalApi` | 通过 Orleans 客户端调用 Grain 的 HTTP API。 |
| `Clients.WorkerService` | 发送模拟传感器更新的后台客户端。 |
| `Dashboard` | 独立部署的仪表板 Silo。 |
| `Infrastructure` | 共享的标识、集群、终结点及遥测配置。 |

这种拆分使客户端和 Silo 能够独立扩展和部署，同时使契约独立于实现。

<a id="run-the-system-locally"></a>

## 在本地运行系统

启动 Azurite，然后在仓库根目录打开四个终端：

```powershell
dotnet run --project .\samples\Deployment\AzureContainerApps\Silo
dotnet run --project .\samples\Deployment\AzureContainerApps\Dashboard
dotnet run --project .\samples\Deployment\AzureContainerApps\Clients.MinimalApi
dotnet run --project .\samples\Deployment\AzureContainerApps\Clients.WorkerService
```

每个启动配置均选择 Development 环境并连接 Azurite。在部署前验证系统：

1. 打开 `Dashboard` 进程输出的仪表板 URL，确认仪表板 Silo 和专用 Silo 均处于活动状态。
1. 在 Minimal API 上调用 `GET /hello/0`，确认返回问候语。
1. 调用 `GET /providers`，确认出现 Grain 键 `0`。
1. 停止后台工作进程并确认 API 调用仍然正常；后台工作进程和 Minimal API 是独立的 Orleans 客户端，因此重启后台工作进程时，API 客户端仍保持与 Silo 集群的连接。

示例仪表板在本地开发时采用开放访问。请将此终结点限制在受信任的本地主机上。对于已部署的环境，应使用 HTTPS、操作人员身份验证和授权以及私有管理访问路径来 [保护仪表板](../dashboard/index.md#secure-the-dashboard-before-exposing-it)。

<a id="understand-the-production-configuration"></a>

## 理解生产配置

开发环境使用存储模拟器。已部署的进程使用 <xref:Azure.Identity.DefaultAzureCredential>、用户分配的托管标识以及 Azure 表服务 URI。基于令牌的数据平面访问提供存储授权。

每个已部署的 Silo 都有唯一的通告 Silo 终结点和网关终结点。Azure 容器应用在应用边界分配文档所述的终结点，因此示例将每个 Silo 部署为单副本的独立容器应用。通过添加使用未占用端口的 Silo 应用来增加容量。

分配角色之前，请查看示例的 [部署 README](https://github.com/dotnet/orleans/blob/main/samples/Deployment/AzureContainerApps/README.md) 和 `Azure/bootstrap.bicep`。需要特权的初始化过程与常规部署有意分开。

<a id="configure-the-host-in-code"></a>

### 在代码中配置主机

Orleans 生产配置基于 .NET 通用主机组合而成。[示例 Silo 主机](https://github.com/dotnet/orleans/blob/main/samples/Deployment/AzureContainerApps/Silo/Program.cs) 通过 <xref:Microsoft.Extensions.Configuration.IConfiguration> 读取部署值，调用 <xref:Microsoft.Extensions.Hosting.OrleansSiloGenericHostExtensions.UseOrleans*>，并在得到的 <xref:Orleans.Hosting.ISiloBuilder> 上配置集群标识、终结点和 Azure 表存储集群成员资格。

对于持久化 Grain 状态的应用程序，同一模式也可用于注册持久化 Grain 存储：

:::code language="csharp" source="../snippets/compiled/Deployment/DeploymentSnippets.cs" id="container_apps_storage_usings":::
:::code language="csharp" source="../snippets/compiled/Deployment/DeploymentSnippets.cs" id="configure_container_apps_storage":::

`ServiceId` 对于应用程序保持稳定。`ClusterId` 标识部署环境或蓝绿部署集群。每个 Silo 和外部客户端均使用相同的值及集群成员资格后端。主机从部署配置中读取提供程序终结点和集群标识，缺少必需值时启动失败。

监听终结点描述进程接受连接的位置。通告终结点标识其他 Silo 和客户端用于连接该进程的唯一地址和端口：

:::code language="csharp" source="../snippets/compiled/Deployment/DeploymentSnippets.cs" id="container_endpoint_usings":::
:::code language="csharp" source="../snippets/compiled/Deployment/DeploymentSnippets.cs" id="configure_container_endpoints":::

部署平台为每个 Silo 提供这些值。示例的 [终结点配置](https://github.com/dotnet/orleans/blob/main/samples/Deployment/AzureContainerApps/Infrastructure/OrleansEndpointConfigurationExtensions.cs) 将相同的模型应用于每个单副本容器应用所分配的私有地址及唯一端口对。

<a id="choose-a-deployment-model"></a>

### 选择部署模型

使用网络和生命周期保证与目标环境相匹配的平台指南：

| 目标 | 推荐模型 |
| --- | --- |
| Kubernetes | 通告各 Pod 的 IP，允许 Pod 间直接 TCP 通信，并使用生产级集群提供程序。 |
| 托管容器平台 | 为每个 Silo 提供文档明确说明的每实例地址，或唯一的私有地址和端口对。 |
| 虚拟机或裸机 | 通告稳定的私有地址，并将 .NET 主机作为长期运行的服务进行监管。 |
| Azure App Service | 使用多实例示例及其私有的每实例端口映射。 |

将示例改造为其他主机环境之前，请参阅 [平台要求](../deployment/platform-guides.md)。必须始终保证每条成员资格记录都指向一个其他所有 Silo 可直接访问的 Silo 终结点。

<a id="deploy"></a>

## 部署

1. Fork Orleans 仓库并启用 GitHub Actions。
1. 配置限定于你的 Fork 和受保护的 `azure-container-apps` 环境的 GitHub OIDC 标识。
1. 运行示例中一次性的特权初始化过程，创建容器镜像注册表、成员资格存储、运行时标识和最小权限角色分配。
1. 将 `samples/Deployment/AzureContainerApps/deployment/deploy.yml` 复制到 `.github/workflows/deploy-orleans-container-apps.yml`。
1. 配置仪表板主机和入口，以实现上文描述的操作人员访问控制。
1. 添加示例 README 中列出的环境变量，然后运行工作流。

工作流构建镜像、推送不可变的 Git SHA 标签、按镜像摘要部署，并通过 GitHub OIDC 和工作负载标识进行身份验证。

<a id="verify-the-deployment"></a>

## 验证部署

通过仅限操作人员的管理路径连接，然后：

1. 确认所有预期的 Silo 在仪表板中均处于活动状态。
1. 测试 `GET /hello/0`、`GET /hello/255` 和 `GET /providers`。
1. 验证无效的 Grain 键返回 HTTP 400。
1. 确认启动、就绪及存活探针均处于正常状态。
1. 检查 Application Insights 中的链路跟踪和日志，确认请求跨越 API 到 Grain 的边界。

将该系统改造为生产应用之前，请逐项完成 [生产就绪检查清单](../deployment/production-readiness.md)，配置 [持久化 Grain 存储](../grains/grain-persistence/index.md)，并规划 [优雅关闭与升级](../deployment/upgrades.md)。
