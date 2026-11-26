---
title: "部署和规模 a Orleans 应用 Azure"
description: "主办方和比例 a Orleans 应用 Azure 集装箱容器 Azure 集装箱登记和 Azure 表格储存或 Azure 宇宙 DB NoSQL."
ms.topic: how-to
ms.devlang: csharp
ms.date: 08/02/2026
zone_pivot_groups: orleans-persistence-option
# CustomerIntent: 开发者希望在 Azure 上托管 Orleans 应用，利用数据库和应用服务的扩缩容功能。
---

<a id="deploy-and-scale-an-orleans-app-on-azure"></a>

# 部署和规模 a Orleans 应用 Azure

快速启动,你部署和调整 Orleans URL 缩短程序 Azure 容器应用程序。 该应用程序允许用户向应用程序提交完整的 URL , 返回一个缩短的版本, 以便与其他人共享, 将它们传送到原始网站 。 Orleans 和 Azure 提供安装像 URL 缩短器等高流量应用程序所需的可缩放性功能。 Orleans 也符合支持 .NET.

快速启动结束时, 您有一个在 Azure 运行的可缩放应用程序, 以提供 URL 缩短功能。 在学习到 :

- 拉动 Azure 开发者 CLI 模板
- 将 Orleans 应用程序安装到 Azure
- 将应用程序缩放到多个实例

<a id="prerequisites"></a>

## 先决条件

- 具有有效订阅功能的 Azure 账户。 [创建免费账户](https://azure.microsoft.com/pricing/purchase-options/azure-account?cid=msft_learn)
- [Azure 开发者 CLI](https://learn.microsoft.com/azure/developer/azure-developer-cli/install-azd)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [嵌入器](https://www.docker.com/)

<a id="get-and-deploy-the-sample-application"></a>

## 获取并部署样本应用

样本应用程序作为 Azure 开发器 CLI 模板可用。 通过此快速启动: 您拉动模板应用程序, 将模板和样本代码部署到 Azure, 修改模板以实施您首选的持久性 grain, 部署必要的资源, 然后部署最后应用程序 。

1. 在空目录中打开终端 。

1. 认证到 Azure 开发者 CLI 使用 `azd auth login`。遵循工具指定的步骤,以验证 CLI 使用首选 Azure 全权证书。

   ```azuredeveloper
   azd auth login
   ```

1. 使用 AZD 模板 `orleans-url-shortener` 和 `azd init` 命令获取样本应用程序。

   ```azuredeveloper
   azd init --template orleans-url-shortener
   ```

1. 初始化时, 配置一个独特的环境名称 。

   > [!TIP]
   > 环境名称也将用作目标资源组的名称。 对于此快速启动, 请考虑使用 `msdocs-orleans-url-shortener`。

1. 部署 Azure 宇宙 DB NoSQL 账户使用 `azd up`Bicep 模板还安装了一个网络应用程序样本。

   ```azuredeveloper
   azd up
   ```

1. 在供给过程中,选择您的订阅和所需位置。等待供给和部署过程完成。该过程大约需要5分钟 ****。

1. 一旦提供您的 Azure 资源,输出中将包含运行中的网络应用程序的 URL。

   ```output
   Deploying services (azd deploy)

     (✓) Done: Deploying service web
   - Endpoint: <https://[container-app-sub-domain].azurecontainerapps.io>

   SUCCESS: Your application was provisioned and deployed to Azure in 5 minutes 0 seconds.
   ```

1. 使用控制台中的 URL 浏览浏览器中您 Web 应用程序的导航。

   :::image type="content" source="media/deploy-scale-orleans-on-azure/web-application.png" alt-text="运行中的 URL 缩短器网络应用程序的截图 。":::

1. 在浏览器地址栏中测试 `shorten` 通过添加 URL 路径等 `/shorten?url=https://www.microsoft.com`页面应重新装入并提供一个新的URL,并在结尾处提供一条缩短路径。将新 URL 复制到剪贴板。

   ```output
   {
     "original": "https://www.microsoft.com",
     "shortened": "http://<container-app-name>.<deployment-name>.<region>.azurecontainerapps.io:<port>/go/<generated-id>"
   }
   ```

1. 将缩短的 URL 粘贴到地址栏中, 按下输入。 该页面应该重新装入并重定向到您指定的 URL 上。

<a id="deploy-extra-services"></a>

## 部署额外服务

最初部署仅部署了主机 URL 缩短程序所需的最起码服务。 要使用 Azure 数据服务来保持 grain 持久性, 您必须首先配置模板以部署首选服务 。

:::zone pivot="azure-storage"

1. 使用终端运行 `azd env set` 配置 `DEPLOY_AZURE_TABLE_STORAGE` 环境变量,以便能够部署 Azure 表格存储。

   ```azuredeveloper
   azd env set DEPLOY_AZURE_TABLE_STORAGE true
   ```

:::zone-end

:::zone pivot="azure-cosmos-db-nosql"

1. 使用终端终端,运行 `azd env set` 配置 `DEPLOY_AZURE_COSMOS_DB_NOSQL` 环境变量,以启用 Azure 宇宙 DB NoSQL.

   ```azuredeveloper
   azd env set DEPLOY_AZURE_COSMOS_DB_NOSQL true
   ```

:::zone-end

1. 运行 `azd provision` 以将您的应用程序架构与新配置相调换。 等待供给进程完成。 程序大约需要两分钟 ****。

   ```azuredeveloper
   azd provision
   ```

   > [!TIP]
   > 或者,您可以再次运行 `azd up` ,这将提供您的架构并重新配置您的应用程序。

<a id="install-nuget-packages"></a>

## 安装 NuGet 软件包

在添加共享提供者之前, 请将模板与片段匹配 :

1. 将工作目录更改为 _./src/web/_。

   ```bash
   cd ./src/web
   ```

1. 在项目文件中,将 `TargetFramework` 改为 `net10.0`。

1. 在 _./src/web/Dockerfiles_中,更新两个 [.NET 容器图像](https://learn.microsoft.com/dotnet/core/docker/container-images):在建设阶段使用 `mcr.microsoft.com/dotnet/sdk:10.0` ,在运行时间阶段使用 `mcr.microsoft.com/dotnet/aspnet:10.0` 。

1. 升级 Orleans 主机软件包:

   ```dotnetcli
   dotnet package add Microsoft.Orleans.Server --version 10.2.2
   ```

下一步,安装相应的 `Microsoft.Orleans.Clustering.*` 和 `Microsoft.Orleans.Persistence.*` NuGet 软件包。这些服务使用基于角色的访问控制进行无密码认证,所以您也必须导入 `Azure.Identity` NuGet 软件包。

:::zone pivot="azure-storage"

1. 导入 `Azure.Identity` 软件包 NuGet:

   ```dotnetcli
   dotnet package add Azure.Identity
   ```

1. 导入 `Microsoft.Orleans.Clustering.AzureStorage` 和 `Microsoft.Orleans.Persistence.AzureStorage` 软件包。

   | 特征         | NuGet 套货包                                |
   |-----------------|----------------------------------------------|
   | **组合** | `Microsoft.Orleans.Clustering.AzureStorage`  |
   | **持久性** | `Microsoft.Orleans.Persistence.AzureStorage` |

   ```dotnetcli
   dotnet package add Microsoft.Orleans.Clustering.AzureStorage --version 10.2.2
   dotnet package add Microsoft.Orleans.Persistence.AzureStorage --version 10.2.2
   ```

:::zone-end

:::zone pivot="azure-cosmos-db-nosql"

1. 导入 `Azure.Identity` 软件包 NuGet:

   ```dotnetcli
   dotnet package add Azure.Identity
   ```

1. 导入 `Microsoft.Orleans.Clustering.Cosmos` 和 `Microsoft.Orleans.Persistence.Cosmos` 软件包。

   | 特征         | NuGet 套货包                          |
   |-----------------|----------------------------------------|
   | **组合** | `Microsoft.Orleans.Clustering.Cosmos`  |
   | **持久性** | `Microsoft.Orleans.Persistence.Cosmos` |

   ```dotnetcli
   dotnet package add Microsoft.Orleans.Clustering.Cosmos --version 10.2.2
   dotnet package add Microsoft.Orleans.Persistence.Cosmos --version 10.2.2
   ```

:::zone-end

<a id="configure-and-redeploy-the-sample-app"></a>

## 配置并重新调配样本应用程序

样本应用程序最初创建了一个本地主机群集, 并保持了 grain 状态。 当位于 Azure时, Orleans 可以使用共享数据服务来组成群集和保持耐用 grain 状态 。

1. 查找并删除现有 `builder` _src/web/ 中的设置代码Program.cs_ 文件。

   :::code source="snippets/url-shortener/orleansurlshortener/Program.cs" id="orleans-configuration":::

:::zone pivot="azure-storage"

1. 添加所需的 `using` 指令:

   :::code source="snippets/deploy-scale-orleans-on-azure/azure-storage/Program.cs" id="azure-storage-usings":::

1. 将 `builder` 配置替换为此处示例,用于实施这些关键概念:

   - 添加有条件的环境检查,以确保应用程序在当地开发和 Azure 托管情景中运行正常。
   - <xref:Orleans.Hosting.AzureTableClusteringExtensions.UseAzureStorageClustering*?displayProperty=nameWithType> 配置 Orleans 组,使用 Azure 表格存储和认证 <xref:Azure.Identity.DefaultAzureCredential>。
   - 配置指派 <xref:Orleans.Configuration.ClusterOptions.ClusterId> 和 <xref:Orleans.Configuration.ClusterOptions.ServiceId>。
   - <xref:Orleans.Configuration.ClusterOptions.ClusterId> identifies a cluster so its clients and silos can communicate. Use a different value for deployments that must remain isolated.
   - <xref:Orleans.Configuration.ClusterOptions.ServiceId> identifies the application and should remain consistent across deployments.

    :::code source="snippets/deploy-scale-orleans-on-azure/azure-storage/Program.cs" id="azure-storage-configuration":::

:::zone-end

:::zone pivot="azure-cosmos-db-nosql"

1. 添加所需的 `using` 指令:

   :::code source="snippets/deploy-scale-orleans-on-azure/cosmos/Program.cs" id="cosmos-usings":::

1. 将 `builder` 配置替换为此处示例,用于实施这些关键概念:

   - 添加有条件的环境检查,以确保应用程序在当地开发和 Azure 托管情景中运行正常。
   - <xref:Orleans.Hosting.HostingExtensions.UseCosmosClustering*?displayProperty=nameWithType> 配置 Orleans 要使用的组群 Azure 宇宙 DB NoSQL 和使用 <xref:Azure.Identity.DefaultAzureCredential>.
   - 配置指派 <xref:Orleans.Configuration.ClusterOptions.ClusterId> 和 <xref:Orleans.Configuration.ClusterOptions.ServiceId>。
   - <xref:Orleans.Configuration.ClusterOptions.ClusterId> identifies a cluster so its clients and silos can communicate. Use a different value for deployments that must remain isolated.
   - <xref:Orleans.Configuration.ClusterOptions.ServiceId> identifies the application and should remain consistent across deployments.

   :::code source="snippets/deploy-scale-orleans-on-azure/cosmos/Program.cs" id="cosmos-configuration":::

:::zone-end

1. 运行运行 `azd deploy` 将应用程序代码重新定位为 docker 容器。等待部署进程完成。该程序可以进行 **大约一分钟**.

   ```azuredeveloper
   azd deploy
   ```

   > [!TIP]
   > 或者,您可以再次运行 `azd up` ,这将提供您的架构并重新配置您的应用程序。

<a id="verify-the-apps-behavior"></a>

## 验证应用程序的行为

校验您更新过的代码是否有效, 方法是再次使用已部署的应用程序, 并检查其存储数据的位置 。

1. 在浏览器地址栏中测试 `shorten` 通过添加 URL 路径等 `/shorten?url=https://learn.microsoft.com/dotnet/orleans`页面应重新装入并提供一个新的URL,并在结尾处提供一条缩短路径。将新 URL 复制到剪贴板。

   ```output
   {
     "original": "https://learn.microsoft.com/dotnet/orleans",
    "shortened": "http://<container-app-name>.<deployment-name>.<region>.azurecontainerapps.io:<port>/go/<generated-id>"
   }
   ```

1. 将缩短的 URL 粘贴到地址栏中, 按下输入。 该页面应该重新装入并重定向到您指定的 URL 上。

可以选择,您可以核实数据组和国家数据是否按照您创建的存储账户中预期的那样存储。

1. 在 Azure 门户网站中,导航到在快速启动时部署的资源小组。

   > [!IMPORTANT]
   > 在此快速启动中早些时候指定的环境名称也是目标资源组的名称 。

:::zone pivot="azure-storage"

1. 导航至 Azure 存储账户概览页面。

1. 在导航中,选择 **存储浏览器**。

1. 展开 **表** 导航项目,以发现由 Orleans创建的两个表格:

   - **OrleansGrainState**:本表格储存持久性状态 grain 应用程序用于处理 URL 重定向的数据。
   - **OrleansSiloInstances**:此表格的曲目 silo 数据的数据 Orleans 组。

1. 选择 **OrleansGrainState** 表格。该表为在测试期间应用过程中持续的所有 URL 重新定向保留的一个行条目。

   :::image type="content" source="media/deploy-scale-orleans-on-azure/storage-table-entities.png" alt-text="屏幕截图显示 Orleans 数据以 Azure 桌子储存。":::

:::zone-end

:::zone pivot="azure-cosmos-db-nosql"

1. 导航至 Azure 宇宙DB NoSQL 账户的概览页。

1. 在导航中,选择 **数据探索器**。

1. 观察本指南早些时候建立的下列集装箱:

   - **OrleansStorage**:本表格储存持久性状态 grain 应用程序用于处理 URL 重定向的数据。

   - **OrleansCluster**:此表格的曲目 silo 数据的数据 Orleans 组。

:::zone-end

<a id="scale-the-app"></a>

## 缩放应用程序

不要增加由此创建的单 silo 容器 App的复制数
快速启动。 Orleans 要求每个 silo 都发布一个独特、可直接获取的广告
端点对配,而 Azure 容器应用不提供稳定的每个复制
一个应用程序的地址 。

为了扩大这一地形图表的规模,再部署一个复制件 silo 具有
广告公布的终点。见 [部署 Orleans 至 Azure 集装箱应用软件](../deployment/deploy-to-azure-container-apps.md)
支持的地形、联网要求和升级程序。

<a id="related-content"></a>

## 相关内容

:::zone pivot="azure-storage"

- [Azure 表格储存](https://learn.microsoft.com/azure/storage/tables/table-storage-overview)
- [Azure 储存 grain 持久性](../grains/grain-persistence/azure-storage.md)

:::zone-end

:::zone pivot="azure-cosmos-db-nosql"

- [Azure 宇宙 DB NoSQL](https://learn.microsoft.com/azure/cosmos-db/nosql/overview)
- [Azure 宇宙DB grain 持久性](../grains/grain-persistence/azure-cosmos-db.md)

:::zone-end

<a id="next-step"></a>

## 下一步

> [! div 类=" 超后"]
> [Grain 持久性](../grains/grain-persistence/index.md)
