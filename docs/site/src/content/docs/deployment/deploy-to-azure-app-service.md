---
title: 在 Windows 上将 Orleans 部署到 Azure App Service
description: 使用专用实例终结点和托管标识，在 Windows Azure App Service 上部署并运维 Orleans 集群。
ms.date: 08/06/2026
ms.topic: tutorial
ms.custom: devops
---

<a id="deploy-orleans-to-azure-app-service-on-windows"></a>

# 在 Windows 上将 Orleans 部署到 Azure App Service

本教程将 [Orleans 购物车示例](https://github.com/dotnet/orleans/tree/main/samples/Deployment/AzureAppService) 作为多实例集群部署到 [Windows Azure App Service](https://learn.microsoft.com/azure/app-service/overview)。请先阅读通用的 [Azure App Service 部署目标总览](azure-app-service.md)。关于 Linux 计划、运行时、启动和 Easy Auth 的差异，请参阅[在 Linux 上将 Orleans 部署到 Azure App Service](deploy-to-azure-app-service-linux.md)。

该示例在每个工作实例中共同托管 ASP.NET Core Blazor 应用和 Orleans Silo。它使用：

- 包含三个工作实例的 Premium v3 Windows App Service 计划。
- 区域虚拟网络集成，以及每个实例一个专用 Silo TCP 端口。
- 用于 Orleans 成员资格和 Grain 状态的 Azure Table Storage。
- 使用 Microsoft Entra 授权的用户分配托管标识，不使用存储账户密钥。
- App Service 身份验证以及用于商品目录变更的 `ProductAdministrator` 应用角色。
- 相互独立的生产槽和暂存槽集群。
- 运行状况检查、启动预热、限时优雅关闭、Application Insights 和 Log Analytics。
- 通过 OpenID Connect（OIDC）向 Azure 进行身份验证的 GitHub Actions 工作流。

<a id="understand-the-topology"></a>

## 了解拓扑

Orleans Silo 通过长时间保持的 TCP 连接直接与各个 Silo 通信。App Service 的 HTTP 负载均衡器不提供这种连接能力。

[区域虚拟网络集成](https://learn.microsoft.com/azure/app-service/overview-vnet-integration) 为每个工作实例提供专用地址，保存在 `WEBSITE_PRIVATE_IP` 中。设置 `vnetPrivatePortsCount: 1` 后，App Service 通过 `WEBSITE_PRIVATE_PORTS` 提供动态分配的端口。只读的 `WEBSITE_INSTANCE_ID` 将作为诊断所用的 Orleans Silo 名称。有关平台设置，请参阅 [App Service 环境变量参考](https://learn.microsoft.com/azure/app-service/reference-app-settings)。应用执行以下操作：

1. 通告专用地址及分配的 Silo 端口。
1. 在所有本地接口上侦听，因为通告地址可能无法在本地绑定。
1. 禁用 Orleans 客户端网关，因为共同托管的 Web 应用使用 Silo 的本地客户端。
1. 使用 Azure Table Storage 作为外部集群成员资格提供程序及持久化 Grain 存储。
1. 分别将生产槽和暂存槽集成到各自的委派子网。

:::code language="csharp" source="../snippets/compiled/Deployment/DeploymentSnippets.cs" id="configure_app_service_endpoints":::

每个 Silo 都必须能访问所有已通告的 Silo 终结点。禁用网关还可以确保商品目录修改操作通过受 Easy Auth 保护的 Web 应用进入系统，而非通过未经身份验证的 Orleans 客户端连接进入。

Orleans 网关不是面向最终用户的身份验证边界。如果应用需要外部 Orleans 客户端，仅为完全受信任的应用客户端启用并通告第二个专用端口。将不受信任的客户端置于经过身份验证的 API 之后，并在修改 Grain 状态的操作处强制执行授权检查。

> [!IMPORTANT]
> 虚拟网络集成主要用于出站连接。投入生产之前，应在已经横向扩容的计划上验证专用端口行为：确认每个工作实例均获得专用 Silo 端口，并且能够连接所有已通告的终结点。

使用支持虚拟网络集成和部署槽的专用层级。每个集成子网的容量应至少达到计划最大扩容规模的两倍；对于需要为替换工作实例留出空间的新部署，`/26` 是实用的最小子网规模。

<a id="prerequisites"></a>

## 先决条件

- Azure 订阅，以及创建资源和角色分配的权限。
- [由 `global.json` 选定的 .NET SDK](https://dotnet.microsoft.com/download)。
- 安装了 Bicep 的 [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli)。
- [`dotnet/orleans`](https://github.com/dotnet/orleans) 仓库的克隆副本。
- 用于 [App Service 身份验证](https://learn.microsoft.com/azure/app-service/overview-authentication-authorization) 的 Microsoft Entra 应用注册。

设置本地部署参数：

```powershell
$location = "westus3"
$resourceGroup = "orleans-shopping-cart"
$appName = "<globally-unique-lowercase-name-up-to-16-characters>"
$authenticationTenantId = "<microsoft-entra-tenant-id>"
$authenticationClientId = "<app-registration-client-id>"
$authenticationClientSecret = "<app-registration-client-secret>"
```

<a id="configure-user-authentication"></a>

## 配置用户身份验证

在 App Service 身份验证所用的应用注册中：

1. 添加值为 `ProductAdministrator` 的应用角色，允许的成员类型包括用户或组。
1. 创建客户端机密。
1. 通过企业应用程序向商品管理员分配该角色。
1. 添加生产槽和暂存槽的 Web 重定向 URI，均以 `/.auth/login/aad/callback` 结尾。

确切的主机名是部署输出，因此可以在完成预配之后、用户登录之前添加重定向 URI。

Easy Auth 允许匿名访问店面，并在 `X-MS-CLIENT-PRINCIPAL` 中注入经过身份验证的声明。在非容器化 Windows 应用上，平台身份验证组件是 IIS 原生模块。应用仅接受 Entra（`aad`）主体，限制并解析该标头，保护 `/products` 路由，隐藏未经授权的导航入口，并在服务层对每次商品修改重新执行授权检查。

解析器信任 App Service 对令牌进行验证并移除伪造的外部身份标头。不要在不受信任的反向代理之后复用此解析器，也不要暴露能够绕过身份验证、直接访问应用进程的路径。

<a id="run-locally"></a>

## 本地运行

```powershell
dotnet run `
  --project .\samples\Deployment\AzureAppService\Silo\Orleans.ShoppingCart.Silo.csproj `
  --environment ASPNETCORE_ENVIRONMENT=Development
```

开发环境使用 localhost 集群和内存状态。所有非开发环境均要求设置 `WEBSITE_PRIVATE_IP`、`WEBSITE_PRIVATE_PORTS`、`ORLEANS_SERVICE_ID`、`ORLEANS_CLUSTER_ID`、`ORLEANS_AZURE_STORAGE_URI` 和 `AZURE_CLIENT_ID`。缺少生产配置会导致启动失败。

匿名店面可以在本地运行。由于本地执行环境没有 App Service 信任的主体标头，商品管理功能仍不可用。

<a id="deploy-the-infrastructure"></a>

## 部署基础设施

登录、创建资源组并部署 Windows 入口点：

```azurecli
az login
az group create --name $resourceGroup --location $location

az deployment group create `
  --resource-group $resourceGroup `
  --template-file .\samples\Deployment\AzureAppService\infra\windows\main.bicep `
  --parameters `
    appName=$appName `
    location=$location `
    authenticationTenantId=$authenticationTenantId `
    authenticationClientId=$authenticationClientId `
    authenticationClientSecret=$authenticationClientSecret
```

该模板创建：

- 包含三个工作实例的 Premium v3 Windows 计划和生产槽、暂存槽。
- 具有独立 `/24` 委派子网的虚拟网络。
- 仅允许这些子网访问且禁用共享密钥授权的存储账户。
- 共享的[用户分配托管标识](https://learn.microsoft.com/azure/app-service/overview-managed-identity)，以及 **Storage Table Data Contributor（存储表数据参与者）** 角色分配。
- 两个槽的 App Service 身份验证。
- Log Analytics 和基于工作区的 Application Insights。

身份验证机密作为安全 Bicep 参数以及与部署槽绑定的应用设置。应在其到期之前轮换。对于生产环境，优先使用 Key Vault 引用，并为生产槽和暂存槽分别创建应用注册。

首次部署需要较高权限以完成初始化，因为它会创建数据平面角色分配。常规部署传入 `assignStorageRoles=false`，无需 `roleAssignments/write` 权限。

托管标识的角色分配可能需要几分钟才能传播完成。在标识能够访问表数据之前，应用首次启动可能失败。

<a id="publish-and-deploy-to-staging"></a>

## 发布并部署到暂存槽

```powershell
$publish = Join-Path $env:TEMP "orleans-shopping-cart-publish"
$package = Join-Path $env:TEMP "orleans-shopping-cart.zip"

dotnet publish `
  .\samples\Deployment\AzureAppService\Silo\Orleans.ShoppingCart.Silo.csproj `
  --configuration Release `
  --framework net10.0 `
  --output $publish

Compress-Archive -Path "$publish\*" -DestinationPath $package -Force

az webapp deploy `
  --name $appName `
  --resource-group $resourceGroup `
  --slot "${appName}stg" `
  --type zip `
  --src-path $package `
  --clean true `
  --restart true
```

ZIP 包包含发布目录中的内容，而非发布目录本身。App Service 将其解压到 `D:\home\site\wwwroot`。示例不在此处写入持久化数据；Orleans Grain 状态仍存储在 Azure Table Storage 中。

将生产槽和暂存槽的回调 URL 添加到应用注册中。随后等待暂存部署输出的主机名所对应的以下终结点就绪：

```text
https://<staging-default-hostname>/health/ready
```

<a id="swap-into-production"></a>

## 交换至生产槽

在使用部署槽交换进行 Orleans 发布之前，请先了解 [App Service 部署槽行为](https://learn.microsoft.com/azure/app-service/deploy-staging-slots)。

```azurecli
az webapp deployment slot swap `
  --name $appName `
  --resource-group $resourceGroup `
  --slot "${appName}stg" `
  --target-slot production
```

两个槽均保留稳定的 `ShoppingCartService` 服务 ID。`ORLEANS_CLUSTER_ID` 与槽绑定，各集群分别使用独立的成员资格表和持久化表。交换期间，App Service 将生产设置应用于暂存槽，并在切换流量之前预热每个暂存工作实例。新的 Silo 在流量切换前加入生产集群。

旧版与新版 Silo 会暂时共存。Grain 接口、序列化有效载荷、状态和外部行为必须彼此兼容。对于不兼容的版本，应部署独立的应用和集群 ID；不要让不兼容的集群拥有同一份可变状态。

<a id="configure-github-actions-with-oidc"></a>

## 配置使用 OIDC 的 GitHub Actions

将示例 [`infra/deploy.yml`](https://github.com/dotnet/orleans/blob/main/samples/Deployment/AzureAppService/infra/deploy.yml) 复制到 `.github/workflows/deploy-app-service.yml`。

为受保护的 GitHub `production` 环境创建联合部署标识。要求适当的审核人员批准部署，限制部署分支，不要创建 `--sdk-auth` 凭据或存储 Azure 凭据 JSON 文档。

配置以下 GitHub 环境变量：

| 变量 | 用途 |
| --- | --- |
| `AUTHENTICATION_CLIENT_ID` | App Service 身份验证客户端 ID |
| `AUTHENTICATION_TENANT_ID` | 用户身份验证所用的租户 |
| `AZURE_APP_NAME` | 全局唯一的 App Service 名称 |
| `AZURE_APP_SERVICE_OS` | `windows` |
| `AZURE_CLIENT_ID` | 联合部署标识的客户端 ID |
| `AZURE_RESOURCE_GROUP_LOCATION` | Azure 区域 |
| `AZURE_RESOURCE_GROUP_NAME` | 已存在的目标资源组 |
| `AZURE_SUBSCRIPTION_ID` | 订阅 ID |
| `AZURE_TENANT_ID` | Azure 部署所用的租户 |

将 `AUTHENTICATION_CLIENT_SECRET` 添加为受保护的环境机密。它属于 Easy Auth 应用注册；Azure 部署仍使用短期有效的 OIDC 凭据。

该工作流将 Actions 固定到不可变的提交 SHA，仅授予 `contents: read` 和 `id-token: write` 权限，部署选定的 Bicep 入口点，发布到暂存槽，等待就绪，执行交换并验证生产环境。常规 Azure 部署标识不需要角色分配权限；可在目标资源组授予 Contributor（参与者）角色，更推荐使用权限仅限模板更新资源的自定义角色。

<a id="health-and-shutdown"></a>

## 运行状况与关闭

App Service 将 `/health/ready` 用于启动预热、部署槽预热及[运行状况检查](https://learn.microsoft.com/azure/app-service/monitor-instances-health-check)。仅当主机和 Silo 启动后才返回成功，并在开始关闭时变为不可用。`/health/live` 是开销很低的本地进程检查，特意不依赖共享存储。

主机允许 Orleans 最多花费 30 秒退出成员资格。App Service 可能更早终止工作实例，因此应用的正确性必须能够容忍 Silo 丢失和调用结果不确定的情况。

<a id="security-and-observability"></a>

## 安全性与可观测性

部署强制使用 HTTPS 和 TLS 1.2 或更高版本，禁用 FTPS，禁用存储共享密钥授权，并将存储网络访问限制在集成子网内。托管标识负责表访问授权。

HTTPS 保护公共 Web 终结点。本示例不对专用 Orleans Silo 连接加密。如果威胁模型要求对虚拟网络内的通信加密，请使用网络控制措施并配置 Orleans TLS。

Application Insights 接收请求、依赖项、异常、应用和 Orleans 日志。监视已就绪的工作实例与 Silo 数量、成员资格变化、延迟、拒绝、存储授权及限流、工作实例资源、重启和部署槽操作。

<a id="production-checklist"></a>

## 生产检查清单

| 关注事项 | Windows App Service 的预期结果 |
| --- | --- |
| 拓扑与网络 | 每个工作实例通告其专用实例地址及分配的 Silo 端口，并且都能够连接每个活动成员资格终结点。 |
| 依赖项与数据 | Azure Table 集群成员资格和 Grain 状态使用独立的生产数据，并经过备份和恢复验证。应用在使用提醒器和流时显式配置相应提供程序。 |
| 标识与机密 | 运行时存储访问使用托管标识。App Service 身份验证、常规部署及特权初始化的凭据相互独立，并在到期前轮换。 |
| 运行状况与生命周期 | 运行状况检查、启动预热、部署槽预热、撤销就绪状态及限时关闭主机均反映应用的生命周期。 |
| 扩缩容与弹性 | 计划至少保留三个工作实例及经过验证的备用容量。在负载下测试横向扩容、缩容、工作实例替换、依赖项性能下降及突然丢失。 |
| 升级与回滚 | 兼容版本使用暂存槽交换，并验证混合版本。不兼容版本采用独立应用、集群 ID、状态迁移方案及流量切换。 |
| 可观测性与事件处置 | 将 Application Insights、Log Analytics、Orleans 遥测、工作实例标识、成员资格、提供程序信号及部署槽操作关联起来。 |
| 基础设施交付 | Bicep 和受保护的 GitHub OIDC 工作流可以重现基础设施，并经暂存槽部署不可变的应用制品。 |

为共同托管的 Blazor Server UI 保留 HTTP 客户端关联性。Orleans 传输独立于 HTTP 关联性，使用专用 Silo 终结点。若要在 Windows 与 Linux 之间迁移，请部署独立应用，验证后再转移流量。

有关更全面的指导，请参阅[生产就绪检查清单](production-readiness.md)、[拓扑、网络与集群](networking.md)、[优雅关闭与升级](upgrades.md)和[选择部署目标](choose-deployment-target.md)。
