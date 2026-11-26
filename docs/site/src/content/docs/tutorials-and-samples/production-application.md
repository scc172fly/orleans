---
title: "将 Orleans 应用程序安装到 Azure 容器应用软件"
description: "运行、检查、部署和核查一个多过程 Orleans 申请在 Azure 集装箱装货箱"
ms.date: 08/19/2026
ms.topic: tutorial
---

<a id="deploy-an-orleans-application-to-azure-container-apps"></a>

# 将 Orleans 应用程序安装到 Azure 容器应用软件

此行进将您从空目录带到已部署的可观测目录 Orleans 使用 [Azure 集装箱样本](https://github.com/dotnet/orleans/tree/main/samples/Deployment/AzureContainerApps)应用程序、基础设施和部署工作流程一起进行编译和验证。

完成的系统专门为 silos、HTTP API 和工人客户、 Azure 表格存储群集、管理身份、健康检测、应用透视和一个 Orleans 刀板。这个样本显示组合和部署;加上一个 grain-储存提供者,以保持应用程序状态。

<a id="prerequisites"></a>

## 先决条件

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [基特](https://git-scm.com/downloads)
- 本地集成[紫紫](https://learn.microsoft.com/azure/storage/common/storage-use-azurite)
- 供部署用的 Azure 订阅和 [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli)

<a id="get-the-application"></a>

## 获得申请

从空工作目录 :

```powershell
git clone https://github.com/dotnet/orleans.git
cd orleans
dotnet build .\samples\Deployment\AzureContainerApps\HelloOrleans.sln -c Release
```

在实施这些项目之前先探索项目:

| 项目项目项目项目项目项目项目项目 | 责任 |
| --- | --- |
| `Abstractions` | Grain 接口和由打电话者和 silos共享的序列合同。 |
| `Grains` | Grain 执行和安置政策。 |
| `Silo` | 专用 Orleans 服务器进程。 |
| `Clients.MinimalApi` | An HTTP API 调用 grains 通过 a Orleans 客户端。 |
| `Clients.WorkerService` | 发送模拟传感器更新的背景客户端。 |
| `Dashboard` | 单独部署的仪表板 silo。 |
| `Infrastructure` | 共有身份、集群、终点和遥测配置。 |

这种分离使客户和 silos 规模能够独立部署,并使合同独立于执行。

<a id="run-the-system-locally"></a>

## 本地操作系统

启动 Azurite, 然后打开仓库根的四个终端 :

```powershell
dotnet run --project .\samples\Deployment\AzureContainerApps\Silo
dotnet run --project .\samples\Deployment\AzureContainerApps\Dashboard
dotnet run --project .\samples\Deployment\AzureContainerApps\Clients.MinimalApi
dotnet run --project .\samples\Deployment\AzureContainerApps\Clients.WorkerService
```

每一个发射剖面图选择开发环境, 并连接到 Azurite。 在部署前先验证系统 :

1. 打开由 `Dashboard` 进程打印的仪表板 URL, 确认其 silo 和专用 silo 活动。
1. 调调 `GET /hello/0` 极低的, API 并证实他将回送祝辞。
1. 调用 `GET /providers` 并确认 grain 键 `0` 出现。
1. 停止工人的通话,确认 API 电话继续;工人和最小 API 客户是独立的 Orleans 客户,因此重新启动该工人将 API 客户与 silo 组连接起来。

样本仪表板使用开放访问权进行地方发展。 将终点保留在可信任的本地主机上。 对于部署的环境, [安全仪表板](../dashboard/index.md#secure-the-dashboard-before-exposing-it) 拥有HTTPS、操作员认证和授权以及私人行政途径。

<a id="understand-the-production-configuration"></a>

## 理解生产配置

开发使用存储模拟器。部署程序使用 <xref:Azure.Identity.DefaultAzureCredential> ,具有用户指定的管理特性和 Azure 表格服务 URI。基于数据访问飞机的用品储存授权。

每部署 silo 具有独特的广告 silo 和网关终点。 Azure Apps 指定了应用程序边界的有文件记录的终点,因此样本将分别部署 silo 添加能力,增加 silo 带有未使用的港口的应用程序。

在分配任务前先审查样品 [高级设计设计](https://github.com/dotnet/orleans/blob/main/samples/Deployment/AzureContainerApps/README.md) 和 `Azure/bootstrap.bicep` ,特准靴子和常规部署是故意分开的。

<a id="configure-the-host-in-code"></a>

### 在代码中配置主机

Orleans 生产生产配置 .NET 常规主机 [试样 silo 主机](https://github.com/dotnet/orleans/blob/main/samples/Deployment/AzureContainerApps/Silo/Program.cs) 读取部署值 <xref:Microsoft.Extensions.Configuration.IConfiguration>调,调 <xref:Microsoft.Extensions.Hosting.OrleansSiloGenericHostExtensions.UseOrleans*>,并配置组群身份、端点和 Azure 将由此形成的储存集群 <xref:Orleans.Hosting.ISiloBuilder>.

同样的模式可以登记为可持久的 grain 存储在已持续 grain 状态 :

:::code language="csharp" source="../snippets/compiled/Deployment/DeploymentSnippets.cs" id="container_apps_storage_usings":::
:::code language="csharp" source="../snippets/compiled/Deployment/DeploymentSnippets.cs" id="configure_container_apps_storage":::

`ServiceId` 仍然稳定, 用于应用程序。 `ClusterId` 确定部署环境或蓝色绿色组群。 每个 silo 和外部客户都使用相同的值和相同的组群后端。主机读取部署配置中的供应商端点和组群特性, 当需要的值不存在时启动失败 。

听听端端点描述程序接受连接的位置。 广告端点指定了独特的地址和港口, 而其他的则指定了 silos 客户利用以下手段达到这一目的:

:::code language="csharp" source="../snippets/compiled/Deployment/DeploymentSnippets.cs" id="container_endpoint_usings":::
:::code language="csharp" source="../snippets/compiled/Deployment/DeploymentSnippets.cs" id="configure_container_endpoints":::

部署平台为每个 silo提供这些值。样本的 [终端配置](https://github.com/dotnet/orleans/blob/main/samples/Deployment/AzureContainerApps/Infrastructure/OrleansEndpointConfigurationExtensions.cs) 对私人地址和分配给每个重复的集装箱应用软件的独特端口配对适用同一模式。

<a id="choose-a-deployment-model"></a>

### 选择部署模式

使用其网络和生命周期保障与目标环境匹配的平台指南:

| 具体目标具体目标 | 建议模式 |
| --- | --- |
| Kubernetes | 发布每个舱位IP广告,允许直接舱到船舱TCP,并使用生产集群供应商。 |
| 集装箱管理平台 | 给每个 silo 一个有记录的每份申请地址或独有的私人地址和对端端端口。 |
| 虚拟机器或光金属 | 发布稳定的私人地址,并监督 .NET 主机的长期服务。 |
| Azure 服务处 | 使用多端端口取样及其私人每端端口测绘。 |

在将样本调整为另一个主机之前,请见 [平台要求](../deployment/platform-guides.md) 。变量是每个会员都指一个 silo 终点,所有其他 silos 都可以直接达到。

<a id="deploy"></a>

## 部署部署

1. 叉入 Orleans 仓库并启用 GitHub 动作。
1. 配置 GitHub OIDC 身份, 仅限制在叉子和受保护的 `azure-container-apps` 环境上。
1. 运行样本的一次性特权陷阱,以创建登记处、成员储存、运行时身份和最不享有特权的角色分配。
1. 复制 `samples/Deployment/AzureContainerApps/deployment/deploy.yml` 到 `.github/workflows/deploy-orleans-container-apps.yml`。
1. 配置上面所述的操作员控制仪表板主机和内部。
1. 添加样本 README 中列出的环境变量,然后运行工作流程。

工作流程建立图像,推动不可改变的 Git-SHA 标签,通过图像摘要进行部署,并通过 GitHub OIDC和工作量身份认证。

<a id="verify-the-deployment"></a>

## 核查部署情况

连接通过仅使用操作员的行政路径,然后:

1. 确认每个预期 silo 都在仪表板上活动。
1. 练习 `GET /hello/0`、 `GET /hello/255`和 `GET /providers`。
1. 校验无效的 grain 密钥返回 HTTP 400 。
1. 确认启动、准备和活性探测器是健康的
1. 检查应用观察的痕迹和日志,确认请求跨越 API-至-grain 边界。

在调整生产系统之前,先通过 [生产准备状态清单](../deployment/production-readiness.md)、配置 [耐久 grain 储存](../grains/grain-persistence/index.md)和计划 [宽度停机和升级](../deployment/upgrades.md)进行工作。
