---
title: "在 Windows 上部署 Orleans 到 Azure 应用程序服务"
description: "部署和运行 Orleans Window 窗口集 Azure 使用私人试录终端和有管理身份的应用程序服务。"
ms.date: 08/06/2026
ms.topic: tutorial
ms.custom: devops
---

<a id="deploy-orleans-to-azure-app-service-on-windows"></a>

# 在 Windows 上部署 Orleans 到 Azure 应用程序服务

此教程将 [Orleans 购物车样品](https://github.com/dotnet/orleans/tree/main/samples/Deployment/AzureAppService) 部署为 [Window Azure 服务](https://learn.microsoft.com/azure/app-service/overview)上的多端集聚。 从共享 [Azure 服务处目标概览](azure-app-service.md)开始。 Linux 计划、 运行时间、 启动和 Auth 简单差异见 [部署 Orleans 到 Linux 上的 Azure 应用服务](deploy-to-azure-app-service-linux.md)。

样本共同主持 ASP.NET 核心布拉佐应用软件和 Orleans silo 使用:

- 3名工人的Premium v3 WindowsApp服务计划。
- 区域虚拟网络一体化和每个专用的 silo TCP端口。
- Azure 成员 Orleans 和 grain 状态的表格储存。
- 用户指定的管理身份,具有 Microsoft Entra授权,没有存储账户密钥。
- App 服务验证和目录更改的 `ProductAdministrator` 应用程序功能。
- 分开生产和中转槽集群。
- 健康检查、启动热热设备、封闭式优雅关闭、应用透视和记录分析。
- A GitHub 认证的行动工作流程 Azure 使用 OpenID 连接 (OIDC) 。

<a id="understand-the-topology"></a>

## 理解地形学

Orleans silos 直接与个人联系 silos 长寿命 TCP 连接。 HTTP 程序服务负载平衡器无法提供这种连接 。

[区域虚拟网络一体化](https://learn.microsoft.com/azure/app-service/overview-vnet-integration) 给每个工人在 `WEBSITE_PRIVATE_IP`中的私人地址。有 `vnetPrivatePortsCount: 1`,应用程序服务会暴露 `WEBSITE_PRIVATE_PORTS`中动态分配端口。只读 `WEBSITE_INSTANCE_ID` 将改为诊断的 Orleans silo 名称。平台设置见 [App 服务环境可变参考](https://learn.microsoft.com/azure/app-service/reference-app-settings) 。应用程序:

1. 发布私人地址广告,并分配 silo 端口。
1. 在所有本地界面上监听,因为广告地址可能无法在当地受约束。
1. 禁用 Orleans 客户端网关,因为共同托管的网络应用程序使用 silo本地客户端。
1. 用途 Azure 表格作为外部集群提供商和耐用 grain 商店。
1. 将生产和中转整合成单独的分管网。

:::code language="csharp" source="../snippets/compiled/Deployment/DeploymentSnippets.cs" id="configure_app_service_endpoints":::

每一 silo 必须在广告上向每个广告者传达 silo 端点。 禁用网关还确保目录突变通过易经保护的网络应用程序而不是未经认证的网络应用程序进入 Orleans 客户端连接。

Orleans 网关不是最终用户认证边界。如果应用程序需要外部 Orleans 客户,则只能为完全信任的应用客户启用第二个私人港口并刊登广告。在经过认证的 API背后保持不信任的客户,并在变异 grain 状态的行动中强制实施授权。

> [!IMPORTANT]
> 虚拟网络整合主要是一个外向特征。 在生产前根据一个扩大的计划验证私人港口行为:确认每个工人都得到一个私营 silo 港口,并可以连接到每一个广告的终点。

使用专门的层级支持虚拟网络整合和部署位置。每个整合子网的大小至少是计划最大规模的两倍; `/26` 是新部署的、有替代工人空间的实际最低要求。

<a id="prerequisites"></a>

## 先决条件

- A 音 A Azure 并允许订阅和许可创建资源和任务分配。
- 下 [.NET SDK 选择 `global.json`](https://dotnet.microsoft.com/download).
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) 与比塞普。
- 复制 [`dotnet/orleans`](https://github.com/dotnet/orleans) 仓库的克隆。
- A Microsoft 安装 [App 服务验证](https://learn.microsoft.com/azure/app-service/overview-authentication-authorization)的应用程序登记。

设定本地部署值 :

```powershell
$location = "westus3"
$resourceGroup = "orleans-shopping-cart"
$appName = "<globally-unique-lowercase-name-up-to-16-characters>"
$authenticationTenantId = "<microsoft-entra-tenant-id>"
$authenticationClientId = "<app-registration-client-id>"
$authenticationClientSecret = "<app-registration-client-secret>"
```

<a id="configure-user-authentication"></a>

## 配置用户认证

在应用程序服务认证应用程序登记中:

1. 添加一个应用程序角色,其值为 `ProductAdministrator` ,且允许成员类型包括用户或组。
1. 创建客户秘密。
1. 通过企业应用程序指定产品管理员的作用。
1. 添加生产和中继网络重定向的URIs, 结尾处为 `/.auth/login/aad/callback`。

确切的主机名是部署输出, 这样您可以在提供后和用户签名前添加重置的 URI 。

Easy Auth允许匿名的存储前请求和输入 `X-MS-CLIENT-PRINCIPAL`中的认证索赔。在一个非集装箱窗口应用程序中,平台认证部分是一个本地的IIS模块。应用程序只接受一个主页(`aad`)本、限制和切除信头,保护 `/products` 路径,隐藏未经授权的导航,并重新授权服务层中的每一产品突变。

分析器信任软件服务来验证标记并删除假的外部身份信头。 不要在不信任的反向代理背后再使用它, 或者暴露一个通往应用程序程序的绕行路径 。

<a id="run-locally"></a>

## 本地运行本地

```powershell
dotnet run `
  --project .\samples\Deployment\AzureAppService\Silo\Orleans.ShoppingCart.Silo.csproj `
  --environment ASPNETCORE_ENVIRONMENT=Development
```

开发使用本地主机群集和模拟状态。 每个非发展环境都需要 `WEBSITE_PRIVATE_IP`、 `WEBSITE_PRIVATE_PORTS`、 `ORLEANS_SERVICE_ID`、 `ORLEANS_CLUSTER_ID`、 `ORLEANS_AZURE_STORAGE_URI`和 `AZURE_CLIENT_ID`。 缺失的生产配置无法启动 。

匿名商店前台在当地工作。产品管理仍然无法使用,因为当地执行没有App-Services信任的主要信头。

<a id="deploy-the-infrastructure"></a>

## 部署基础设施

签名, 创建一个资源组, 并部署 Windows 切入点 :

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

模板创建 :

- 3名工人的Premium v3 Windows计划和生产/储存槽。
- 虚拟网络,有单独的 `/24` 授权子网。
- 仅限这些子网的存储账户, 并禁用共享钥匙授权 。
- 共享 [用户指定的管理身份](https://learn.microsoft.com/azure/app-service/overview-managed-identity) 和 **存储表数据贡献器** 任务。
- App Service 服务 两个空档的验证 。
- 日志分析和基于工作空间的应用洞察。

认证秘密是一个安全的 Bicep 参数和一个槽粘性应用程序设置。 在过期前旋转它。 对于制作, 更喜欢使用密钥跳板引用和单独的生产和中转程序注册 。

第一次部署是特权靴子陷阱,因为它创造了数据飞机任务分配。常规部署通过 `assignStorageRoles=false` ,不需要 `roleAssignments/write`。

管理下的身份分配需要几分钟时间才能传播。 第一次应用程序启动可能失败, 直到身份可以访问表格数据 。

<a id="publish-and-deploy-to-staging"></a>

## 出版并部署到中装

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

ZIP 包含发布目录的内容, 而不是目录本身。 AppService 将它提取到 `D:\home\site\wwwroot`。 样本不会写入那里持久的数据; Orleans grain 状态仍然在 Azure 表存储中 。

将生产和播放回调的 URL 添加到应用程序注册中。 然后等待 :

```text
https://<staging-default-hostname>/health/ready
```

<a id="swap-into-production"></a>

## 投入生产

审查审查 [服务处部署时空行为](https://learn.microsoft.com/azure/app-service/deploy-staging-slots) 在使用空格转换 Orleans 展开

```azurecli
az webapp deployment slot swap `
  --name $appName `
  --resource-group $resourceGroup `
  --slot "${appName}stg" `
  --target-slot production
```

两个空档都保留稳定的 `ShoppingCartService` 服务编号。 `ORLEANS_CLUSTER_ID` 的时档是粘性,每个组群使用单独的会籍和持久性表。在交换中,应用服务处将生产设置用于交替之前的组合并让每一个中转工人都暖和起来。新的 silos 在切换前加入生产组群。

旧的和新的 silos 暂时共存。 Grain 接口、序列式有效载荷、状态和外部行为必须相互兼容。对于不相容的释放,请使用单独的应用程序和集束识别码;不允许不相容的组群拥有相同的变异状态。

<a id="configure-github-actions-with-oidc"></a>

## 配置 GitHub OIDC 动作

复制样本 [`infra/deploy.yml`](https://github.com/dotnet/orleans/blob/main/samples/Deployment/AzureAppService/infra/deploy.yml) 到 `.github/workflows/deploy-app-service.yml`。

创建受保护的联邦部署身份 GitHub `production` 要求适当的审查人员,限制部署分支机构,不创建 `--sdk-auth` 证明或储存 Azure 全权证书 JSON 文件。

配置这些 GitHub 环境变量:

| 变量 | 目的目的宗旨 |
| --- | --- |
| `AUTHENTICATION_CLIENT_ID` | App 服务验证客户身份 |
| `AUTHENTICATION_TENANT_ID` | 用户认证租户 |
| `AZURE_APP_NAME` | 全球唯一全球通用的应用程序服务名称 |
| `AZURE_APP_SERVICE_OS` | `windows` |
| `AZURE_CLIENT_ID` | 联邦部署身份身份身份身份身份身份身份 |
| `AZURE_RESOURCE_GROUP_LOCATION` | Azure 区域 |
| `AZURE_RESOURCE_GROUP_NAME` | 现有目标资源组 |
| `AZURE_SUBSCRIPTION_ID` | 订阅ID |
| `AZURE_TENANT_ID` | Azure 部署的租户 |

添加 `AUTHENTICATION_CLIENT_SECRET` 作为受保护的环境秘密。它属于易用自用软件登记; Azure 部署仍然使用寿命短的OIDC证书。

工作流程将行动钉在不可改变的空格中,只给 `contents: read` 和 `id-token: write`,部署选定的比塞普切入点,发布准备、等待准备、交换和核实生产。其常规 Azure 身份不需要指定角色许可;给予目标资源组贡献者,或最好仅对模板更新的资源进行定制。

<a id="health-and-shutdown"></a>

## 健康和停机

App Service 使用 `/health/ready` 来启动热身、空档热身和 [健康检查](https://learn.microsoft.com/azure/app-service/monitor-instances-health-check)。它只有在主机和 silo 启动后才成功,当关闭开始时便无法使用。 `/health/live` 是廉价的本地流程检查,有意不依赖共享存储 。

最多30秒 Orleans 申请服务处可以提前解雇工人,因此申请的正确性必须能够容忍 silo 和未知的通话结果。

<a id="security-and-observability"></a>

## 安全和遵守

部署强制执行HTTPS、TLS 1.2或以后,使FTPS失效,使存储共享钥匙授权失效,并限制储存网络进入整合子网。管理下的身份授权访问表格。

HTTPS 保护公共网络端点。 私有 Orleans silo 连接不由此样本加密。 当威胁模式需要在虚拟网络内加密时, 请使用网络控制并配置 Orleans TLS 。

应用程序透视接收请求、依赖性、例外、应用和 Orleans 日志。监控待命工人和 silo 计数、会籍变更、延时、拒绝、存储授权/批发、工人资源、重新启动和空档业务。

<a id="production-checklist"></a>

## 生产清单

| 关注问题 | 视窗应用程序服务结果 |
| --- | --- |
| 地形学和联网 | 每个工人都登广告,宣传其私人网站地址并分配 silo 港口,每个工人都可以与每一个积极成员端点连接。 |
| 依赖和数据 | Azure 表格群集和 grain 状态使用单独生产数据,并附有测试后备份和恢复。应用程序在使用时明确配置提醒和流源提供者。 |
| 身份和机密 | 实时储存存取使用管理身份,应用程序服务认证、例行部署和特权靴套证书保持单独,在到期前轮换。 |
| 健康和生命周期 | 健康检查、启动热热设备、空档热设备、准备状态清除和捆绑主机关闭反映了应用生命周期。 |
| 规模和复原力 | 该计划至少保留了三名工人和经过测试的剩余能力,在负荷下进行规模扩大、规模化、工人替换、依赖性退化和突然损失。 |
| 升级和回调 | 兼容版本使用中转点交换和混合版本验证。 不兼容版本使用单独的应用程序、集束识别码、州计划和交通中断。 |
| 观察和事件 | 应用透视、日志分析、 Orleans 遥测、工人身份、成员资格、供应商信号和空档业务是相互关联的。 |
| 基础设施交付 | Bicep和受保护的 GitHub OIDC工作流程复制基础设施,通过中转部署不可变应用文物。 |

保持 HTTP 客户端对共同主办的 Blazor 服务器 UI 的亲近性。 Orleans 运输使用私人 silo 终端,而独立于 HTTP 的亲和性。 Windows 和 Linux 之间移动, 使用单独的应用程序并在验证后移动通信 。

更广泛的指导意见见 [生产准备状态清单](production-readiness.md)、 [地形学、联网和集群](networking.md)、 [优优优停机和升级](upgrades.md)和 [选择部署目标](choose-deployment-target.md)。
