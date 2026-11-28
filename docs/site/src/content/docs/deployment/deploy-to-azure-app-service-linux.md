---
title: 在 Linux 上将 Orleans 部署到 Azure App Service
description: 使用专用实例终结点和托管标识，在 Linux Azure App Service 上部署并运维 Orleans 集群。
ms.date: 08/06/2026
ms.topic: tutorial
ms.custom: devops
---

<a id="deploy-orleans-to-azure-app-service-on-linux"></a>

# 在 Linux 上将 Orleans 部署到 Azure App Service

本教程将 [Orleans 购物车示例](https://github.com/dotnet/orleans/tree/main/samples/Deployment/AzureAppService) 部署到 [Linux Azure App Service 内置的 .NET 技术栈](https://learn.microsoft.com/azure/app-service/configure-language-dotnetcore)。请先阅读通用的 [Azure App Service 部署目标总览](azure-app-service.md)。应用、存储、标识、网络、运行状况和 OIDC 设计与 [Windows 指南](deploy-to-azure-app-service.md) 相同，但 Linux 计划、运行时、启动和 Easy Auth 行为需要单独验证。

<a id="understand-the-linux-topology"></a>

## 了解 Linux 拓扑

Bicep 入口点 `infra/linux/main.bicep` 创建：

- 设置了 `kind: linux` 和 `reserved: true` 的 Premium v3 Linux 计划。
- 使用 `linuxFxVersion: DOTNETCORE|10.0` 的 Linux Web 应用和暂存槽。
- 三个工作实例，每个实例共同托管 ASP.NET Core 和一个 Orleans Silo。
- 生产槽和暂存槽的虚拟网络集成子网。
- 使用托管标识授权的 Azure Table Storage 集群成员资格和持久化 Grain 状态。
- App Service 身份验证、Application Insights、Log Analytics、运行状况检查和预热设置。

App Service 的 HTTP 前端无法路由 Orleans Silo 间的连接。每个工作实例读取 `WEBSITE_PRIVATE_IP` 和 `WEBSITE_PRIVATE_PORTS` 的第一个值，并通告该动态分配的 Silo 终结点。使用只读的 `WEBSITE_INSTANCE_ID` 作为诊断所用的 Orleans Silo 名称。由于无法保证通告地址能在本地绑定，它会在所有本地接口上侦听：

:::code language="csharp" source="../snippets/compiled/Deployment/DeploymentSnippets.cs" id="configure_app_service_endpoints":::

专用地址和端口属于文档所述的通用 App Service 设置，并非 Windows 专属设置。不过，Microsoft 的文档未保证 Linux 上针对 Orleans 的特定端口映射行为。因此部署验证是必需的：

1. 横向扩容到至少三个工作实例。
1. 确认每个工作实例都收到不同的 `WEBSITE_PRIVATE_IP` 以及至少一个 `WEBSITE_PRIVATE_PORTS` 值。
1. 检查 Orleans 成员资格，确认每个 Silo 均通告了这些值。
1. 测试所有已通告的 Silo 终结点之间的双向 TCP 连接。
1. 在横向扩容、缩容、重启及部署槽交换期间重复上述验证。

> [!IMPORTANT]
> 区域虚拟网络集成主要提供出站连接能力。不要认为仅启用该功能就能证明入站专用端口可以访问。

示例禁用 Orleans 客户端网关，因为 Web 应用使用共同托管的 Silo 的本地客户端。这可以防止专用 Orleans 客户端绕过商品目录修改操作所需的 Easy Auth 和 `ProductAdministrator` 检查。Orleans 网关不是面向最终用户的身份验证边界。如果需要外部 Orleans 客户端，仅为完全受信任的应用客户端启用第二个专用端口；不受信任的调用方应置于经过身份验证的 API 之后。

<a id="linux-specific-requirements"></a>

## Linux 特定要求

使用支持虚拟网络集成和部署槽的专用 Standard、Premium 或 Isolated 层级。示例使用 Premium v3 和三个工作实例。

部署前，查询目标区域的内置 Linux 运行时：

```azurecli
az webapp list-runtimes --os linux
```

确认输出包含模板字面量 `DOTNETCORE|10.0` 所表示的 .NET 版本。运行时可用性因云环境和区域而异。如果该标记不可用，应同时将运行时和目标框架的字面量更新为受支持的版本。

Linux 的启动基于容器。内置 .NET 技术栈无需自定义启动命令，即可启动依赖框架的 ZIP 部署。模板设置 `SCM_DO_BUILD_DURING_DEPLOYMENT=false`，因此 Oryx 使用预先发布的二进制文件，而不是还原依赖项并重新生成。ZIP 部署将发布输出解压到区分大小写的 `/home/site/wwwroot` 路径，对应 Windows 上的 `D:\home\site\wwwroot`。示例不依赖任一路径，也不在 App Service 文件系统中存储持久化状态。

[App Service 身份验证](https://learn.microsoft.com/azure/app-service/overview-authentication-authorization) 作为 Ambassador 边车运行，而非 Windows 的进程内模块，但两个平台注入相同的已验证主体标头。`WEBSITE_WARMUP_PATH`、`WEBSITE_WARMUP_STATUSES` 和 `WEBSITE_SWAP_WARMUP_PING_PATH` 仍然适用。有关这些设置，请参阅 [App Service 环境变量参考](https://learn.microsoft.com/azure/app-service/reference-app-settings)。如果启动确实超出平台限制，应在受支持的范围内调整 `WEBSITES_CONTAINER_START_TIME_LIMIT`，而不是在 Orleans 启动前返回就绪状态。

<a id="prerequisites"></a>

## 先决条件

- Azure 订阅，以及创建资源和角色分配的权限。
- [由 `global.json` 选定的 .NET SDK](https://dotnet.microsoft.com/download)。
- 安装了 Bicep 的 [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli)。
- [`dotnet/orleans`](https://github.com/dotnet/orleans) 仓库的克隆副本。
- 用于 App Service 身份验证的 Microsoft Entra 应用注册和客户端机密。

设置部署参数：

```powershell
$location = "westus3"
$resourceGroup = "orleans-shopping-cart-linux"
$appName = "<globally-unique-lowercase-name-up-to-16-characters>"
$authenticationTenantId = "<microsoft-entra-tenant-id>"
$authenticationClientId = "<app-registration-client-id>"
$authenticationClientSecret = "<app-registration-client-secret>"
```

<a id="configure-authentication-and-authorization"></a>

## 配置身份验证和授权

在 App Service 身份验证的应用注册中创建 `ProductAdministrator` 应用角色，将其分配给商品管理员，并添加以 `/.auth/login/aad/callback` 结尾的生产槽和暂存槽 Web 重定向 URI。

店面允许匿名流量。商品管理在路由和导航层要求具备应用角色，并在修改 Grain 状态前立即重新检查角色。身份验证处理程序仅接受由 App Service 注入且大小受限的 Entra 主体标头。它本身不会验证令牌，因此不要通过绕过 Easy Auth 的路径暴露应用。

确切的生产槽和暂存槽主机名是 Bicep 的输出。在预配完成之后、登录之前，可以添加其回调 URI。

<a id="deploy-the-linux-infrastructure"></a>

## 部署 Linux 基础设施

```azurecli
az login
az group create --name $resourceGroup --location $location

az deployment group create `
  --resource-group $resourceGroup `
  --template-file .\samples\Deployment\AzureAppService\infra\linux\main.bicep `
  --parameters `
    appName=$appName `
    location=$location `
    authenticationTenantId=$authenticationTenantId `
    authenticationClientId=$authenticationClientId `
    authenticationClientSecret=$authenticationClientSecret
```

模板为每个工作实例请求一个专用 Silo 端口，将生产槽和暂存槽分配至独立的委派 `/24` 子网，并将存储网络访问限制在这些子网内。模板禁用存储共享密钥授权，并向应用的用户分配标识授予 **Storage Table Data Contributor（存储表数据参与者）** 角色。

首次部署需要资源创建权限及受限的角色分配权限。后续部署可使用 `assignStorageRoles=false`。标识的角色分配可能需要几分钟才能传播完成。

身份验证机密标记为 `@secure()`，并存储为与部署槽绑定的应用设置。应在到期前轮换。对于加强安全性的部署，优先使用 Key Vault 引用，并为生产槽和暂存槽分别创建应用注册。

<a id="publish-and-deploy"></a>

## 发布和部署

同一个依赖框架的包可以在 Windows 和 Linux 上运行：

```powershell
$publish = Join-Path $env:TEMP "orleans-shopping-cart-linux-publish"
$package = Join-Path $env:TEMP "orleans-shopping-cart-linux.zip"

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

等待暂存部署输出的主机名上的 `/health/ready` 就绪，将两个回调 URL 添加到应用注册中，然后交换部署槽：

```azurecli
az webapp deployment slot swap `
  --name $appName `
  --resource-group $resourceGroup `
  --slot "${appName}stg" `
  --target-slot production
```

两个槽均保留稳定的 `ShoppingCartService` 服务 ID。`ORLEANS_CLUSTER_ID` 与槽绑定，各集群具有独立的成员资格表和持久化表。在 HTTP 流量切换前，暂存工作实例接收生产设置，并预热、加入生产 Orleans 集群。新旧 Silo 暂时共存，因此版本之间必须保持传输协议、序列化器、状态及行为兼容。

对于不兼容的版本，应部署独立的应用和集群 ID。不要将现有 App Service 计划从 Windows 切换到 Linux，也不要让不兼容的集群拥有同一份可变状态。

<a id="deploy-with-github-oidc"></a>

## 使用 GitHub OIDC 部署

复制 [`infra/deploy.yml`](https://github.com/dotnet/orleans/blob/main/samples/Deployment/AzureAppService/infra/deploy.yml)。配置受保护的 `production` 环境，要求审核人员批准部署，并限制部署分支：

| 名称 | 类型 | 值 |
| --- | --- | --- |
| `AUTHENTICATION_CLIENT_ID` | 变量 | Easy Auth 应用注册的客户端 ID |
| `AUTHENTICATION_CLIENT_SECRET` | 机密 | Easy Auth 应用注册的客户端机密 |
| `AUTHENTICATION_TENANT_ID` | 变量 | 用户身份验证所用的租户 |
| `AZURE_APP_NAME` | 变量 | 全局唯一的应用名称 |
| `AZURE_APP_SERVICE_OS` | 变量 | `linux` |
| `AZURE_CLIENT_ID` | 变量 | 联合部署标识的客户端 ID |
| `AZURE_RESOURCE_GROUP_LOCATION` | 变量 | Azure 区域 |
| `AZURE_RESOURCE_GROUP_NAME` | 变量 | 已存在的资源组 |
| `AZURE_SUBSCRIPTION_ID` | 变量 | 订阅 ID |
| `AZURE_TENANT_ID` | 变量 | Azure 部署所用的租户 |

Azure 部署标识使用 OIDC，而非存储的 Azure 凭据。Easy Auth 机密具有独立用途，作为安全 Bicep 参数传入。常规部署标识不需要角色分配权限；可在资源组上授予 Contributor（参与者）角色，或授予权限范围更窄的自定义部署角色。工作流部署 Linux 入口点，发布到暂存槽，等待就绪，交换部署槽并验证生产环境。

<a id="health-shutdown-and-observability"></a>

## 运行状况、关闭和可观测性

- `/health/ready` 仅在 .NET 主机和 Orleans Silo 启动后返回成功，并在关闭前变为不可用。
- `/health/live` 仅检查本地进程，不会在共享存储中断期间重启所有工作实例。
- 主机允许 Orleans 最多花费 30 秒退出成员资格。Linux App Service 仍可能提前停止容器，因此 Grain 调用必须能够容忍结果不确定和 Silo 丢失。
- Application Insights 收集 ASP.NET Core 请求、依赖项、异常、应用及 Orleans 日志。运维查询应包含 `WEBSITE_INSTANCE_ID`、集群 ID、槽及部署元数据。
- 监视已就绪的工作实例和 Silo 数量、成员资格变化、容器启动及重启、CPU 与内存、请求延迟、Orleans 拒绝、存储故障及部署槽操作。

<a id="security-and-platform-constraints"></a>

## 安全性与平台限制

模板要求 HTTPS、TLS 1.2 或更高版本，禁用 FTPS，并使用托管标识访问存储，无需账户密钥。Blazor Server 保持启用客户端关联性；Orleans 不依赖 HTTP 关联性。

HTTPS 在 App Service 前端终止。示例的专用 Orleans Silo 连接未加密。如果虚拟网络不足以构成信任边界，请添加 Orleans TLS。

App Service 可能在未提供完整关闭时间的情况下替换工作实例。集成子网的容量应满足计划的扩容规模并留有替换余量，至少保留三个工作实例，并在负载下验证故障恢复和滚动升级。持久化 Grain 状态应独立于成员资格数据进行备份。

<a id="production-checklist"></a>

## 生产检查清单

| 关注事项 | Linux App Service 的预期结果 |
| --- | --- |
| 拓扑与网络 | 每个工作实例通告其专用实例地址及分配的 Silo 端口，并且都能够连接每个活动成员资格终结点。 |
| 依赖项与数据 | Azure Table 集群成员资格和 Grain 状态使用独立的生产数据，并经过备份和恢复验证。应用在使用提醒器和流时显式配置相应提供程序。 |
| 标识与机密 | 运行时存储访问使用托管标识。App Service 身份验证、常规部署及特权初始化的凭据相互独立，并在到期前轮换。 |
| 运行状况与生命周期 | 容器启动、运行状况检查、部署槽预热、撤销就绪状态及限时关闭主机均反映应用的生命周期。 |
| 扩缩容与弹性 | 计划至少保留三个工作实例及经过验证的备用容量。在负载下测试横向扩容、缩容、工作实例替换、依赖项性能下降及突然丢失。 |
| 升级与回滚 | 兼容版本使用暂存槽交换，并验证混合版本。不兼容版本采用独立应用、集群 ID、状态迁移方案及流量切换。 |
| 可观测性与事件处置 | 将 Application Insights、Log Analytics、Orleans 遥测、工作实例标识、成员资格、提供程序信号、容器事件及部署槽操作关联起来。 |
| 基础设施交付 | Bicep 和受保护的 GitHub OIDC 工作流可以重现基础设施，并经暂存槽部署不可变的应用制品。 |

有关通用运维指导，请参阅[生产就绪检查清单](production-readiness.md)、[拓扑、网络与集群](networking.md)、[运行状况与可观测性](health-and-observability.md)、[优雅关闭与升级](upgrades.md)和[选择部署目标](choose-deployment-target.md)。
