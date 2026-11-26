---
title: "部署 Orleans 到 Linux 上的 Azure 应用服务"
description: "部署和运行 Orleans Linux 集的集成 Azure 使用私人试录终端和有管理身份的应用程序服务。"
ms.date: 08/06/2026
ms.topic: tutorial
ms.custom: devops
---

<a id="deploy-orleans-to-azure-app-service-on-linux"></a>

# 部署 Orleans 到 Linux 上的 Azure 应用服务

此教程将 [Orleans 购物车样品](https://github.com/dotnet/orleans/tree/main/samples/Deployment/AzureAppService) 调用到 [built-in .NET Linux 上的堆叠 Azure 服务处处长](https://learn.microsoft.com/azure/app-service/configure-language-dotnetcore)。 从共享 [Azure 服务处目标概览](azure-app-service.md)开始。应用程序、 存储、 身份、 网络、 健康和 OIDC 设计与 [Window 指南](deploy-to-azure-app-service.md)共享, Linux 计划、 运行时间、 启动和轻松的 Auth 行为则分别得到确认 。

<a id="understand-the-linux-topology"></a>

## 理解林努的地形学

比塞普切入点 `infra/linux/main.bicep` 创建:

- 附加 `kind: linux` 和 `reserved: true`的Purium v3 Linux计划。
- 使用 `linuxFxVersion: DOTNETCORE|10.0`的 Linux 网络应用程序和中转位置 。
- 三名工人,每个工人共同担任 ASP.NET 核心和1个 Orleans silo的东道主。
- 制作和播放虚拟网络一体化子网。
- Azure 表格存储群集和经管理身份授权的耐用 grain 状态。
- App 服务验证、 应用程序透视、 日志分析、 健康检查和暖化设置 。

App Service 的 HTTP 前端无法连接 Orleans silo- 到silo 。 每个工人读 `WEBSITE_PRIVATE_IP` , 并且第一个值在 `WEBSITE_PRIVATE_PORTS`中, 然后广告动态分配 silo 终点。 它使用只读 `WEBSITE_INSTANCE_ID` 作为诊断的 Orleans silo 名称。 它会听所有本地界面, 因为所发布地址不能保证本地可绑定 :

:::code language="csharp" source="../snippets/compiled/Deployment/DeploymentSnippets.cs" id="configure_app_service_endpoints":::

私人地址和端口是文件化的通用应用程序服务设置,而不是只使用 Windows 的设置。 但是, Microsoft 没有记录 Orleans- 特定的 Linux 映射保证。 将部署验证视为强制性的 :

1. 规模至少为三名工人。
1. 确认每个工人都得到一个 `WEBSITE_PRIVATE_IP` 和至少一个 `WEBSITE_PRIVATE_PORTS` 价值。
1. 检查 Orleans 成员,确认每个 silo 都公布这些价值。
1. 每个广告 silo 终点之间测试双向TCP连接。
1. 在扩大、缩放、重新启用和换档期间重复。

> [!IMPORTANT]
> 区域虚拟网络一体化主要是外向连接。 不要假设仅允许它就能够进入就能进入私人港口。

样本禁用 Orleans 客户端端关卡,因为网络应用程序使用共同主机 silo阻止私人客户 Orleans 客户绕过 AADER 和 `ProductAdministrator` 围绕目录变异进行检查。 Orleans 网关不是终端用户验证边界。如果外部 Orleans 需要客户客户,只能为完全信任应用程序客户提供第二个私人端口;在认证后保持未信任的来电人 API.

<a id="linux-specific-requirements"></a>

## 具体Linux要求

使用专门的标准、 平价或单级支持虚拟网络整合和部署槽。 样本使用 Premium v3 和 3 名工人 。

使用前, 请询问目标区域 Linux 的内置运行时间:

```azurecli
az webapp list-runtimes --os linux
```

确认产出包括 .NET 模板所代表版本 `DOTNETCORE|10.0` 文字。 运行时间随云和地区而异。 如果没有该符号, 则将机械运行时间和目标框架的文字一起更新为支持版本 。

Linux 启动基于容器。 内置 .NET 堆栈开始使用基于框架的 ZIP 部署, 没有自定义启动命令。 模板设置了 `SCM_DO_BUILD_DURING_DEPLOYMENT=false` , 所以 Oryx 运行预公布的二进制文件而不是恢复和重建它们。 ZIP 部署将发布输出提取到对案件敏感的路径 `/home/site/wwwroot` , 与 Windows `D:\home\site\wwwroot` 相比。 样本并不取决于应用程序服务系统的位置或存储耐久状态 。

[App 服务验证](https://learn.microsoft.com/azure/app-service/overview-authentication-authorization) 运行为大使侧车而不是 Windows 在程序模块,但两个平台都输入了相同的认证主要信头。 `WEBSITE_WARMUP_PATH`、 `WEBSITE_WARMUP_STATUSES`和 `WEBSITE_SWAP_WARMUP_PING_PATH` 仍然适用。请参见这些设置的 [App 服务环境可变参考](https://learn.microsoft.com/azure/app-service/reference-app-settings) 。如果启动合法超过平台限制,则在支持范围内调整 `WEBSITES_CONTAINER_START_TIME_LIMIT` ,而不是在 Orleans 开始前恢复准备状态。

<a id="prerequisites"></a>

## 先决条件

- A 音 A Azure 并允许订阅和许可创建资源和任务分配。
- 下 [.NET SDK 选择 `global.json`](https://dotnet.microsoft.com/download).
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli) 与比塞普。
- 复制 [`dotnet/orleans`](https://github.com/dotnet/orleans) 仓库的克隆。
- A Microsoft 加入应用程序注册和客户保密,供 App Service 认证使用。

设定部署值:

```powershell
$location = "westus3"
$resourceGroup = "orleans-shopping-cart-linux"
$appName = "<globally-unique-lowercase-name-up-to-16-characters>"
$authenticationTenantId = "<microsoft-entra-tenant-id>"
$authenticationClientId = "<app-registration-client-id>"
$authenticationClientSecret = "<app-registration-client-secret>"
```

<a id="configure-authentication-and-authorization"></a>

## 配置认证和授权

在 App Service 认证注册中创建 `ProductAdministrator` 应用程序,将其分配给产品管理员,并添加生产/按住Web重定向 URIs, 直至 `/.auth/login/aad/callback`。

存储端允许匿名传输。 产品管理要求在路径和导航层上设置应用程序功能, 并在 grain- state 突变前立即重新检查它。 认证处理器只接受被绑的 App- service 输入 Entra 主信头。 它不验证标记本身, 所以不要通过绕过 Auth 路径来暴露该应用程序 。

生产和中继主机名是 Bicep 输出。 您可以在提供后和签署前添加回调 URI 。

<a id="deploy-the-linux-infrastructure"></a>

## 部署Linux基础设施

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

模板要求每个工人有一个私人 silo 港口,指定生产和中转为单独的授权的 `/24` 子网,限制储存网络进入这些子网,禁止存储共享钥匙授权并给予应用程序用户指定的身份 **存储表数据贡献器**。

首次部署需要资源创造和有限角色分配许可,以后部署可使用 `assignStorageRoles=false`。身份分配可能需要几分钟时间才能推广。

认证秘密标记 `@secure()` ,并存储为槽粘性应用程序设置。 在过期前旋转它。 优先使用密钥空划引用和单独制作/ 保存应用程序登记以进行硬化部署 。

<a id="publish-and-deploy"></a>

## 出版和部署

Windows和Linux 上运行的同一基于框架的软件包:

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

等待 `/health/ready`的中继输出主机名, 在程序注册时添加回调 URL 和交换 :

```azurecli
az webapp deployment slot swap `
  --name $appName `
  --resource-group $resourceGroup `
  --slot "${appName}stg" `
  --target-slot production
```

两个空档都保留稳定的 `ShoppingCartService` 服务标识。 `ORLEANS_CLUSTER_ID` 是固定的,每个组群都有单独的成员和持久性表。 中转工人在HTTP交通开关之前接受生产设置并进入生产 Orleans 集群。老的和新的 silos 暂时共存,因此释放必须保持电线、连环、状态和行为兼容性。

对于不兼容的释放, 请使用单独的应用程序和集束识别码 。 不要将现有的 App Service 计划从 Windows 转换为 Linux , 也不允许不兼容的组拥有相同的变异状态 。

<a id="deploy-with-github-oidc"></a>

## 与 GitHub OIDC部署

复制 [`infra/deploy.yml`](https://github.com/dotnet/orleans/blob/main/samples/Deployment/AzureAppService/infra/deploy.yml)。配置一个受保护的 `production` 环境,与所需审查员和限制部署分支配置:

| 姓名名称名称 | 型 方 | 价值值值 |
| --- | --- | --- |
| `AUTHENTICATION_CLIENT_ID` | 变量 | Easy Auth 应用程序登记客户身份 |
| `AUTHENTICATION_CLIENT_SECRET` | 秘密机密 | Easy Auth Aut-Ause AP - Amit-登记客户秘密 |
| `AUTHENTICATION_TENANT_ID` | 变量 | 用户认证租户 |
| `AZURE_APP_NAME` | 变量 | 全球唯一应用程序名 |
| `AZURE_APP_SERVICE_OS` | 变量 | `linux` |
| `AZURE_CLIENT_ID` | 变量 | 联邦部署身份身份身份身份身份身份身份 |
| `AZURE_RESOURCE_GROUP_LOCATION` | 变量 | Azure 区域 |
| `AZURE_RESOURCE_GROUP_NAME` | 变量 | 现有资源组 |
| `AZURE_SUBSCRIPTION_ID` | 变量 | 订阅ID |
| `AZURE_TENANT_ID` | 变量 | Azure 部署的租户 |

Azure 部署身份使用 OIDC, 而不是存储的 Azure 证书。 易认证秘密有单独的目的, 并被作为安全的比塞普参数传递。 常规身份不需要角色分配许可; 给资源组或较窄的自定义部署角色提供贡献者。 工作流程将Linux 入口部署、 发布到中转站、 等待准备状态、 交换和验证生产 。

<a id="health-shutdown-and-observability"></a>

## 健康、停机和可观察性

- `/health/ready` 只有在 .NET 主机和 Orleans silo 启动并在关闭前无法使用后才返回成功。
- `/health/live` 并且不会在共享的仓储中断期间重新启用所有工人。
- 东道主允许最多30秒的 Orleans 退出会籍。 Linux App Service仍然可以更快地阻止一个容器,因此 grain 电话必须容忍未知的结果和 silo 损失。
- 应用程序 Insights 收集了 ASP.NET 核心请求、依赖性、例外、应用和 Orleans 日志。在业务查询中包括 `WEBSITE_INSTANCE_ID`、群集识别、空档和部署元数据。
- 监测即时工人/silo 计数、成员变动、集装箱起动和重新启动、CPU/模拟、请求暂不、 Orleans 拒绝、储存故障和空档操作。

<a id="security-and-platform-constraints"></a>

## 安全和平台制约

模板要求 HTTPS 、 TLS 1.2 或以后使用, 禁用 FTPS, 并使用管理身份来存储没有账户密钥的密钥。 Blazor 服务器仍然启用客户亲近性; Orleans 不取决于 HTTP 的亲和性 。

HTTPS 终止在 App Service 前端。 样本的私有 Orleans silo 连接没有加密。 如果虚拟网络不够信任边界, 请添加 Orleans TLS 。

应用程序服务可以取代工人而不提供完全停机间隔。 规模整合子网用于计划的规模加更替能力,至少保留三名工人,并验证负载中的故障恢复和滚动升级。 支持不依赖会员数据的耐用 grain 状态。

<a id="production-checklist"></a>

## 生产清单

| 关注问题 | Linux Apper 服务结果 |
| --- | --- |
| 地形学和联网 | 每个工人都登广告,宣传其私人网站地址并分配 silo 港口,每个工人都可以与每一个积极成员端点连接。 |
| 依赖和数据 | Azure 表格群集和 grain 状态使用单独生产数据,并附有测试后备份和恢复。应用程序在使用时明确配置提醒和流源提供者。 |
| 身份和机密 | 实时储存存取使用管理身份,应用程序服务认证、例行部署和特权靴套证书保持单独,在到期前轮换。 |
| 健康和生命周期 | 集装箱启动、健康检查、空位暖化、准备状态拆除和封闭式主机关闭反映了应用的生命周期。 |
| 规模和复原力 | 该计划至少保留了三名工人和经过测试的剩余能力,在负荷下进行规模扩大、规模化、工人替换、依赖性退化和突然损失。 |
| 升级和回调 | 兼容版本使用中转点交换和混合版本验证。 不兼容版本使用单独的应用程序、集束识别码、州计划和交通中断。 |
| 观察和事件 | 应用透视、日志分析、 Orleans 遥测、工人身份、成员资格、供应商信号、集装箱事件和空档业务是相互关联的。 |
| 基础设施交付 | Bicep和受保护的 GitHub OIDC工作流程复制基础设施,通过中转部署不可变应用文物。 |

关于共享业务指导,见 [生产准备状态清单](production-readiness.md)、 [地形学、联网和集群](networking.md)、 [健康和可观察性](health-and-observability.md)、 [优优优停机和升级](upgrades.md)和 [选择部署目标](choose-deployment-target.md)。
