<a id="orleans-shopping-cart-on-azure-app-service"></a>

# Azure App Service 上的 Orleans 购物车

本示例将多实例 Orleans 购物车部署到 Windows 或 Linux [Azure App Service](https://learn.microsoft.com/azure/app-service/overview)。它演示了：

- 三个 App Service 工作实例，每个实例共同托管 ASP.NET Core 应用与 Orleans Silo。
- 通过 `WEBSITE_PRIVATE_IP` 和 `WEBSITE_PRIVATE_PORTS` 发现各实例的 Orleans 终结点。
- 生产和暂存环境使用独立的虚拟网络集成子网。
- 通过用户分配的托管标识访问 Azure Table Storage，实现集群成员资格管理及 Grain 状态存储。
- App Service 运行状况检查、启动预热、优雅关闭及基于部署槽的逐步发布。
- Microsoft Entra 身份验证以及面向商品管理的应用角色授权。
- Application Insights 日志及请求/依赖项遥测。
- 通过 OpenID Connect（OIDC）实现 GitHub Actions 部署，而不使用部署客户端机密。

> [!IMPORTANT]
> 仅对 App Service 的 HTTP 服务进行横向扩展，并不足以满足 Orleans 的要求。本示例为每个实例分配一个专用 Silo 端口，并使用区域虚拟网络集成，使 Silo 能通过 TCP 直接连接。

<a id="choose-a-platform"></a>

## 选择平台

两个入口都调用 `infra/flex` 下的共享模块，并部署同一个应用：

| 平台 | 模板 | 平台专用配置 |
| --- | --- | --- |
| Windows | `infra/windows/main.bicep` | Windows App Service 计划及 `netFrameworkVersion` |
| Linux | `infra/linux/main.bicep` | Linux 计划，含 `reserved: true` 和 `linuxFxVersion` |

两种平台均监听所有本地网络接口，同时通告动态分配的专用地址和端口。每个工作实例都使用只读的 `WEBSITE_INSTANCE_ID` 作为 Orleans Silo 名称，以便诊断。将任一拓扑用于生产前，请先横向扩展，并验证所有通告的 Silo 终结点之间能通过 TCP 直接连通。

ZIP 部署在 Windows 上将发布文件放入 `D:\home\site\wwwroot`，在 Linux 上放入 `/home/site/wwwroot`。应用不依赖这些路径，也不在其中持久化 Grain 状态；持久化状态存储在 Azure Table Storage 中。

<a id="run-locally"></a>

## 在本地运行

仓库的 `global.json` 选定所需的 .NET SDK。

```powershell
dotnet run `
  --project .\Silo\Orleans.ShoppingCart.Silo.csproj `
  --environment ASPNETCORE_ENVIRONMENT=Development
```

开发模式有意使用 localhost 集群配置和内存状态存储。其他任何环境均要求提供 App Service 专用终结点变量及 Azure Storage 配置；绝不会回退到单主机生产集群。

本地运行时，店面仍可访问，但无法使用商品管理功能，因为本地执行没有 App Service Authentication 注入的可信 `X-MS-CLIENT-PRINCIPAL` 请求头。

<a id="configure-microsoft-entra-authentication"></a>

## 配置 Microsoft Entra 身份验证

为 [App Service Authentication](https://learn.microsoft.com/azure/app-service/overview-authentication-authorization) 创建 Microsoft Entra 应用注册：

1. 添加值为 `ProductAdministrator` 的应用角色，并允许用户或组使用。
1. 创建客户端机密，并记录其值。
1. 通过企业应用程序将该应用角色分配给商品管理员。
1. 添加生产环境和暂存环境的 Web 重定向 URI，以 `/.auth/login/aad/callback` 结尾。

生产和暂存环境的准确主机名由部署输出提供。可以先部署基础设施，再添加这些重定向 URI，然后部署应用。

App Service Authentication 允许匿名访问店面。应用仅信任平台注入的主体请求头，保护 `/products` 路由，向未获授权的用户隐藏相应导航项，并在修改 Grain 状态之前，在 `ProductService` 中再次执行授权检查。Silo 禁用了 Orleans 客户端网关，因此无法通过 Orleans 客户端直接连接来绕过这些检查并修改商品目录。

<a id="deploy-manually"></a>

## 手动部署

安装 [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli)，登录并选择操作系统：

```powershell
$location = "westus3"
$resourceGroup = "orleans-shopping-cart"
$appName = "<globally-unique-lowercase-name-up-to-16-characters>"
$operatingSystem = "linux" # Use "windows" for Windows App Service.
$authenticationTenantId = "<microsoft-entra-tenant-id>"
$authenticationClientId = "<app-registration-client-id>"
$authenticationClientSecret = "<app-registration-client-secret>"

az login
az group create --name $resourceGroup --location $location
```

对于 Linux，请确认目标区域提供所配置的内置 .NET 运行时：

```powershell
az webapp list-runtimes --os linux |
  Select-String "DOTNETCORE"
```

部署所选模板：

```powershell
az deployment group create `
  --resource-group $resourceGroup `
  --template-file ".\infra\$operatingSystem\main.bicep" `
  --parameters `
    appName=$appName `
    location=$location `
    authenticationTenantId=$authenticationTenantId `
    authenticationClientId=$authenticationClientId `
    authenticationClientSecret=$authenticationClientSecret
```

机密参数标记为 `@secure()`，因此 Azure 不会在部署历史记录中保留其值。生产环境应在机密过期前轮换，优先使用 Key Vault 引用，或为生产和暂存环境分别创建应用注册。

初始部署是引导操作。执行该操作的标识必须有权创建资源及角色分配。模板会在示例存储账户上向应用的用户分配托管标识授予 **Storage Table Data Contributor** 角色。

发布并部署到暂存槽：

```powershell
$publish = Join-Path $env:TEMP "orleans-shopping-cart-publish"
$package = Join-Path $env:TEMP "orleans-shopping-cart.zip"

dotnet publish .\Silo\Orleans.ShoppingCart.Silo.csproj `
  --configuration Release `
  --framework net10.0 `
  --output $publish

Compress-Archive -Path "$publish\*" -DestinationPath $package -Force

$stagingSlot = "${appName}stg"

az webapp deploy `
  --name $appName `
  --resource-group $resourceGroup `
  --slot $stagingSlot `
  --type zip `
  --src-path $package `
  --clean true `
  --restart true
```

读取准确的暂存主机名并验证就绪状态：

```powershell
$stagingHost = az webapp show `
  --name $appName `
  --resource-group $resourceGroup `
  --slot $stagingSlot `
  --query defaultHostName `
  --output tsv

Invoke-WebRequest "https://$stagingHost/health/ready"
```

将两个回调 URL 加入应用注册，再将暂存槽交换到生产环境：

```powershell
az webapp deployment slot swap `
  --name $appName `
  --resource-group $resourceGroup `
  --slot $stagingSlot `
  --target-slot production
```

首次部署基础设施后，托管标识的角色分配可能需要数分钟才能传播生效。

<a id="configure-github-actions"></a>

## 配置 GitHub Actions

使用默认的 `assignStorageRoles=true` 完成一次[手动基础设施部署](#deploy-manually)，再使用日常工作流。仓库中的工作流有意传入 `assignStorageRoles=false`，使 OIDC 标识无须获得创建角色分配的权限；它不是引导工作流。

将 [`infra/deploy.yml`](infra/deploy.yml) 复制为 `.github/workflows/deploy-app-service.yml`。

为 `production` 环境配置 [Azure 的 GitHub OIDC 联合身份验证](https://learn.microsoft.com/azure/developer/github/connect-from-azure-openid-connect)，并添加这些环境变量：

| 变量 | 值 |
| --- | --- |
| `AUTHENTICATION_CLIENT_ID` | App Service Authentication 应用注册的客户端 ID |
| `AUTHENTICATION_TENANT_ID` | 用户身份验证所用的 Microsoft Entra 租户 ID |
| `AZURE_APP_NAME` | 全局唯一的 App Service 名称 |
| `AZURE_APP_SERVICE_OS` | `windows` 或 `linux` |
| `AZURE_CLIENT_ID` | 联合部署标识的客户端 ID |
| `AZURE_RESOURCE_GROUP_LOCATION` | Azure 区域 |
| `AZURE_RESOURCE_GROUP_NAME` | 已存在的目标资源组 |
| `AZURE_SUBSCRIPTION_ID` | Azure 订阅 ID |
| `AZURE_TENANT_ID` | Azure 部署所用的 Microsoft Entra 租户 ID |

将 `AUTHENTICATION_CLIENT_SECRET` 添加为 GitHub 环境机密。该机密用于 App Service 用户身份验证；GitHub 的 Azure 部署使用 OIDC，不使用客户端机密。

工作流只请求 `contents: read` 和 `id-token: write` 权限。它使用 `assignStorageRoles=false` 进行部署，因此引导完成后，其 Azure 标识不需要创建角色分配的权限。请在目标资源组上授予 **Contributor** 角色，或优先授予仅限于 Bicep 模板所含资源的自定义角色。

<a id="upgrade-an-existing-windows-deployment"></a>

## 升级已有 Windows 部署

Windows 模板保留已有 App Service 计划和存储账户的名称、`ShoppingCartService` 服务 ID、`Default` 和 `Staging` 集群 ID，以及现有成员资格表和持久化表的名称。

如果已有部署仍使用存储账户密钥，不要先运行完整模板。请按以下顺序迁移：

1. 创建 `${appName}-identity`，将其分配给应用和 `${appName}stg` 槽，并在 `${appName}storage` 上授予其 **Storage Table Data Contributor** 角色。
1. 将 `AZURE_CLIENT_ID`、`ORLEANS_AZURE_STORAGE_URI`、`ORLEANS_SERVICE_ID=ShoppingCartService` 和现有槽专用集群 ID 合并到两个槽中。
1. 将使用托管标识的构建部署到暂存槽，等待就绪后交换到生产环境。
1. 在回滚窗口内保持共享密钥访问启用，因为原来的生产构建现在运行在暂存槽中。
1. 两个槽都运行使用托管标识的构建后，使用 `allowSharedKeyAccess=false assignStorageRoles=false` 部署 `infra/windows/main.bicep`。

不要在 Windows 与 Linux 之间转换已有 App Service 计划。应部署独立应用并迁移流量。

<a id="operational-behavior"></a>

## 运行行为

- 生产环境与暂存环境使用独立的委派子网，以及随部署槽保留的集群 ID。
- 两个槽在各次部署间都保留稳定的 `ShoppingCartService` 服务 ID。
- 每个集群使用独立的 Azure Table 成员资格表及 Grain 状态表。
- 应用仅在主机和 Silo 启动后报告就绪，并在关闭前撤销就绪状态。
- 主机最多允许 30 秒完成 Orleans 关闭。App Service 可能更早终止工作实例，因此正确性必须能够容忍 Silo 丢失及调用结果未知的情况。
- HTTPS 与 TLS 设置保护 HTTP 入站流量。本示例未加密 Orleans 专用 Silo TCP 流量；如果威胁模型要求在虚拟网络内部进行传输加密，请添加 Orleans TLS。
- Application Insights 接收 ASP.NET Core、依赖项、异常、应用及 Orleans 日志。
- Linux App Service 在边车容器中运行 Easy Auth；Windows 使用进程内 App Service 模块。两者注入相同的主体请求头契约。
- `DOTNETCORE|10.0`、`v10.0` 和 `net10.0` 是部署字面值，不是 Orleans 产品版本标识。

完整的平台指南请参阅：

- [将 Orleans 部署到 Windows Azure App Service](https://dotnet.github.io/orleans/docs/deployment/deploy-to-azure-app-service/)
- [将 Orleans 部署到 Linux Azure App Service](https://dotnet.github.io/orleans/docs/deployment/deploy-to-azure-app-service-linux/)

<a id="application-model"></a>

## 应用模型

传给 `GetGrain<IProductGrain>(product.Id)` 的商品 ID 是商品 Grain 的标识。`ProductDetails` 上的 `[Id(n)]` 特性标识 Orleans 版本容错序列化器中的序列化字段，并不定义 Grain 标识。

<a id="acknowledgements"></a>

## 致谢

本示例源自 [IEvangelist/orleans-shopping-cart](https://github.com/IEvangelist/orleans-shopping-cart)，并从 [Azure-Samples/Orleans-Cluster-on-Azure-App-Service](https://github.com/Azure-Samples/Orleans-Cluster-on-Azure-App-Service) 导入。商品管理授权采用了 [Azure-Samples PR #13](https://github.com/Azure-Samples/Orleans-Cluster-on-Azure-App-Service/pull/13) 的纵深防御设计。本目录保留原始 MIT 许可证。
