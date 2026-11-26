<a id="orleans-shopping-cart-on-azure-app-service"></a>

# Orleans 购物车 Azure 服务处处长

此样本会部署多个程序 Orleans 以 Windows 或 Linux 格式购买购物车 [Azure 服务处](https://learn.microsoft.com/azure/app-service/overview)这表明:

- 三名应用程序服务工人共同主持一个 ASP.NET 核心应用和 Orleans silo。
- Per-instance Orleans 从 `WEBSITE_PRIVATE_IP` 和 `WEBSITE_PRIVATE_PORTS`.
- 单独生产和运行虚拟网络整合子网。
- Azure 表格存储组合和 grain 状态使用用户指定的管理身份。
- 健康检查、启动热身、优雅的关闭和基于空档的推出。
- Microsoft 产品管理方面的认证和兼并授权。
- 应用透视记录和请求/依赖遥测。
- GitHub 使用 OpenID 连接(OIDC)而不是部署客户端秘密来部署行动。

> [!IMPORTANT]
> 应用 HTTP 服务扩大规模本身并不足以满足 Orleans 。 样本为每个实例分配一个私人 silo 端口,并使用区域虚拟网络整合,以便 silos 能够直接通过 TCP 连接。

<a id="choose-a-platform"></a>

## 选择平台

两个入境点在 `infra/flex` 下调用共享模块,并部署同样的应用程序:

| 平台平台平台平台 | 模板模板模板模板 | 平台组合 |
| --- | --- | --- |
| 视窗 | `infra/windows/main.bicep` | WindowsApp服务计划和 `netFrameworkVersion` |
| 林勒 | `infra/linux/main.bicep` | Linux计划,与 `reserved: true` 和 `linuxFxVersion` |

两个平台都刊登动态分配的私人地址和港口广告,同时倾听所有本地界面。每个工人使用只读 `WEBSITE_INSTANCE_ID` 的名称作为诊断的 Orleans silo 名称。在使用生产中的两种地貌之前,先扩大范围,核实每个广告的 silo 终点之间的直接TCP可达性。

ZIP 部署后, 将已公布文件存放在 Windows `D:\home\site\wwwroot` 和 Linux `/home/site/wwwroot` 上。 应用程序并不取决于路径或持续状态 grain ; 耐久状态存储于 Azure 表存储中 。

<a id="run-locally"></a>

## 本地运行本地

存储库的 `global.json` 选择所需的 .NET SDK。

```powershell
dotnet run `
  --project .\Silo\Orleans.ShoppingCart.Silo.csproj `
  --environment ASPNETCORE_ENVIRONMENT=Development
```

开发模式有意使用本地主机群集和模拟状态。 其他环境都需要应用程序服务处的私人端点变量和 Azure 存储配置;它永远不会回到单一主机生产集群。

商店前台仍然可以在当地使用,但产品管理无法进行,因为当地执行没有应用服务认证所注入的可信赖 `X-MS-CLIENT-PRINCIPAL` 头。

<a id="configure-microsoft-entra-authentication"></a>

## 配置 Microsoft Entra 认证

创建 Microsoft Entra App 注册 [App 服务验证](https://learn.microsoft.com/azure/app-service/overview-authentication-authorization):

1. 添加一个带有值 `ProductAdministrator` 并允许用户或组的应用程序角色。
1. 创建客户名秘密并记录其价值。
1. 通过企业应用程序指定产品管理员发挥应用功能。
1. 添加生产和中继网络重定向的URIs, 结尾处为 `/.auth/login/aad/callback`。

生产和中继主机名是部署输出。 您可以首先部署基础设施, 添加那些重置的磁共振和再安装应用程序 。

App Servication 验证允许匿名的商店前端交通。 应用程序只信任平台输入的主信头, 保护 `/products` 中,隐藏导航物项,使其不被未经授权的用户使用,并重复授权 `ProductService` 突变前 grain 状态。 silo 禁用其 Orleans 客户端门户, 所以目录突变无法通过直接 Orleans 客户端连接。

<a id="deploy-manually"></a>

## 手动部署

安装 [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli),签名并选择操作系统:

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

对于Linux, 确认目标区域提供配置的内置 .NET 运行时间 :

```powershell
az webapp list-runtimes --os linux |
  Select-String "DOTNETCORE"
```

部署选中模板 :

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

密参数被标记 `@secure()` ,所以 Azure 在部署历史中不会保留其价值。对于生产,在到期前旋转它,并选择键跳跳板参考或单独注册生产和中转的应用程序。

初始部署是一种诱导操作,其身份需要许可才能创建资源和角色分配。模板提供应用程序用户指定的管理身份 **存储表格数据贡献器** 样本存储账户。

发布并部署到中转站:

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

读取确切的中位主机名并验证准备状态:

```powershell
$stagingHost = az webapp show `
  --name $appName `
  --resource-group $resourceGroup `
  --slot $stagingSlot `
  --query defaultHostName `
  --output tsv

Invoke-WebRequest "https://$stagingHost/health/ready"
```

将回调的 URL 添加到应用程序注册中, 然后交换进入生产阶段:

```powershell
az webapp deployment slot swap `
  --name $appName `
  --resource-group $resourceGroup `
  --slot $stagingSlot `
  --target-slot production
```

管理下的身份角色分配在第一批基础设施部署后可能需要几分钟才能推广。

<a id="configure-github-actions"></a>

## 配置 GitHub 动作

完成 [人工手工基础设施安装](#deploy-manually) 一次违约 `assignStorageRoles=true` 在使用常规工作流程之前,入帐工作流程会故意通过 `assignStorageRoles=false` 所以它的身份不需要获得许可 来创造角色分配; 它不是一个陷阱工作流程。

复制 [`infra/deploy.yml`](infra/deploy.yml) 到 `.github/workflows/deploy-app-service.yml`。

配置 [GitHub COIDC 联邦 Azure](https://learn.microsoft.com/azure/developer/github/connect-from-azure-openid-connect) 环境 `production` ,并添加这些环境变量:

| 变量 | 价值值值 |
| --- | --- |
| `AUTHENTICATION_CLIENT_ID` | App Service App Servication 验证应用程序登记客户身份 |
| `AUTHENTICATION_TENANT_ID` | Microsoft 用于用户认证的 Entra 租户身份识别 |
| `AZURE_APP_NAME` | 全球唯一全球通用的应用程序服务名称 |
| `AZURE_APP_SERVICE_OS` | `windows` 或 `linux` |
| `AZURE_CLIENT_ID` | 联邦部署身份的客户身份 |
| `AZURE_RESOURCE_GROUP_LOCATION` | Azure 区域 |
| `AZURE_RESOURCE_GROUP_NAME` | 现有目标资源组 |
| `AZURE_SUBSCRIPTION_ID` | Azure 订阅编号 |
| `AZURE_TENANT_ID` | Microsoft 部署 Azure 的承租人身份证 |

添加添加添加 `AUTHENTICATION_CLIENT_SECRET` 以 a 作为 GitHub 环境机密。 这个秘密用于 App Service 用户验证; GitHub's Azure 使用 OIDC , 不使用客户端秘密 。

工作流程只要求 `contents: read` 和 `id-token: write`。它与 `assignStorageRoles=false`一起部署,因此其 Azure 身份不需要获得许可即可在靴子圈后设立角色分配。请给目标资源组 **贡献者** ,或最好仅对Bicep模板中的资源有定制作用。

<a id="upgrade-an-existing-windows-deployment"></a>

## 更新现有 Windows 部署

视窗模板保留了现有的应用程序服务计划和存储账户名称、 `ShoppingCartService` 服务编号、 `Default` 和 `Staging` 群集标识,以及现有成员和持久性表格名称。

如果现有的部署仍然使用存储账户密钥, 不要先运行完整模板。 按此顺序移动 :

1. 创建 `${appName}-identity`,将其指定到应用程序和 `${appName}stg` 槽中,并准予它 **存储表数据贡献器** 在 `${appName}storage`上。
1. 将 `AZURE_CLIENT_ID`、 `ORLEANS_AZURE_STORAGE_URI`、 `ORLEANS_SERVICE_ID=ShoppingCartService`和现有具体空档组号编号合并为两个空档。
1. 部署管理下的身份结构 进行集结,等待准备状态, 并转换成生产。
1. 保持为回缩窗口启用的共享密钥访问, 因为前生产结构正在运行中。
1. 部署两个空档后, `infra/windows/main.bicep` 与 `allowSharedKeyAccess=false assignStorageRoles=false`.

不要改变Windows和Linux之间的现有应用程序服务计划。 使用单独的应用程序和迁移交通 。

<a id="operational-behavior"></a>

## 操作行为

- 生产和中转使用不同的分权子网和固定档的集群识别码。
- 两个空缺职位在部署期间保留稳定的 `ShoppingCartService` 服务代号。
- 每个组群使用单独的 Azure 表格成员和 grain- 国家表格。
- 应用程序只在主机和 silo 启动后返回准备状态,并在关闭前取消准备状态。
- 最多30秒 Orleans 关闭。 App Service 可以提前解雇工人, 所以正确性必须容忍 silo 和未知的通话结果。
- HTTPS 和 TLS 设置保护 HTTP 入侵。 Orleans 私有 silo TCP 流量不在此样本加密; 如果威胁模式要求在虚拟网络内进行运输加密, 请添加 Orleans TLS 。
- 应用程序 Insight 接收 ASP.NET 核心、扶养、例外、申请和 Orleans 日志。
- Linux App Service在侧车集装箱内操作易读文件; Windows 使用程序内应用程序服务模块。两个软件都输入相同的主信头合同 。
- `DOTNETCORE|10.0`, `v10.0`,和 `net10.0` 是部署字面, 不是 Orleans 产品转换品牌。

完整的平台指导,见:

- [在 Windows 上部署 Orleans 到 Azure 应用程序服务](https://dotnet.github.io/orleans/docs/deployment/deploy-to-azure-app-service/)
- [部署 Orleans 到 Linux 上的 Azure 应用服务](https://dotnet.github.io/orleans/docs/deployment/deploy-to-azure-app-service-linux/)

<a id="application-model"></a>

## 应用模型

产品编号传递给 `GetGrain<IProductGrain>(product.Id)` 是产品 grain身份的身份。 `[Id(n)]` 属性的属性 `ProductDetails` 指定用于 Orleans耐版本的连续播放器; 它们没有定义 grain 身份。

<a id="acknowledgements"></a>

## 致谢

本样本来自 [IEvangelist/orleans-shopping-cart](https://github.com/IEvangelist/orleans-shopping-cart) ,从 [Azure-Samples/Orleans-Cluster-on-Azure-App-Service](https://github.com/Azure-Samples/Orleans-Cluster-on-Azure-App-Service)进口。产品管理授权包括了 [Azure- 样片 PPR #13](https://github.com/Azure-Samples/Orleans-Cluster-on-Azure-App-Service/pull/13)的深入国防设计。本目录保留了麻省理工学院最初许可证。
