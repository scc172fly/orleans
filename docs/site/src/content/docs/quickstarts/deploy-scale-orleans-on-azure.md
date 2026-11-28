---
title: 在 Azure 上部署和扩缩 Orleans 应用
description: 使用 Azure Container Registry 以及 Azure Table Storage 或 Azure Cosmos DB for NoSQL，在 Azure Container Apps 上托管和扩缩 Orleans 应用。
ms.topic: how-to
ms.devlang: csharp
ms.date: 08/02/2026
zone_pivot_groups: orleans-persistence-option
# CustomerIntent: As a developer, I want to host my Orleans application in Azure so that I can take advantage of the scaling capabilities for the database and application services.
---

<a id="deploy-and-scale-an-orleans-app-on-azure"></a>

# 在 Azure 上部署和扩缩 Orleans 应用

本快速入门将 Orleans URL 缩短应用部署到 Azure Container Apps 并进行扩缩容。用户可以向应用提交完整 URL，应用返回缩短后的版本，用户可以与他人共享该地址以跳转至原始站点。Orleans 与 Azure 提供托管 URL 缩短服务等高流量应用所需的可伸缩能力。Orleans 也兼容任何支持 .NET 的其他托管服务。

完成本快速入门后，你将拥有一个在 Azure 上运行的可伸缩 URL 缩短应用。过程中你将学习如何：

- 拉取 Azure Developer CLI 模板
- 将 Orleans 应用部署到 Azure
- 将应用扩展为多个实例

<a id="prerequisites"></a>

## 先决条件

- 具有有效订阅的 Azure 账户。[免费创建账户](https://azure.microsoft.com/pricing/purchase-options/azure-account?cid=msft_learn)。
- [Azure Developer CLI](https://learn.microsoft.com/azure/developer/azure-developer-cli/install-azd)
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Docker](https://www.docker.com/)

<a id="get-and-deploy-the-sample-application"></a>

## 获取并部署示例应用

示例应用以 Azure Developer CLI 模板形式提供。本快速入门将依次拉取模板应用、将模板及示例代码部署到 Azure、修改模板以实现所需的 Grain 持久化、部署必要资源，最后部署最终应用。

1. 在空目录中打开终端。

1. 使用 `azd auth login` 对 Azure Developer CLI 进行身份验证。按照工具指定的步骤，使用所选 Azure 凭据对 CLI 进行身份验证。

   ```azuredeveloper
   azd auth login
   ```

1. 使用 AZD 模板 `orleans-url-shortener` 和 `azd init` 命令获取示例应用。

   ```azuredeveloper
   azd init --template orleans-url-shortener
   ```

1. 初始化过程中，配置唯一的环境名称。

   > [!TIP]
   > 环境名称也将用作目标资源组名称。本快速入门可考虑使用 `msdocs-orleans-url-shortener`。

1. 使用 `azd up` 部署 Azure Cosmos DB for NoSQL 账户。Bicep 模板还会部署示例 Web 应用。

   ```azuredeveloper
   azd up
   ```

1. 在预配过程中，选择订阅和所需位置。等待预配及部署完成。该过程可能需要**大约五分钟**。

1. Azure 资源预配完成后，输出中将包含正在运行的 Web 应用的 URL。

   ```output
   Deploying services (azd deploy)

     (✓) Done: Deploying service web
   - Endpoint: <https://[container-app-sub-domain].azurecontainerapps.io>

   SUCCESS: Your application was provisioned and deployed to Azure in 5 minutes 0 seconds.
   ```

1. 使用控制台中的 URL，在浏览器中访问 Web 应用。

   :::image type="content" source="media/deploy-scale-orleans-on-azure/web-application.png" alt-text="运行中的短网址 Web 应用界面。":::

1. 在浏览器地址栏中添加 URL 路径，例如 `/shorten?url=https://www.microsoft.com`，以测试 `shorten` 终结点。页面应重新加载并提供一个末尾带有缩短路径的新 URL。将新 URL 复制到剪贴板。

   ```output
   {
     "original": "https://www.microsoft.com",
     "shortened": "http://<container-app-name>.<deployment-name>.<region>.azurecontainerapps.io:<port>/go/<generated-id>"
   }
   ```

1. 将缩短后的 URL 粘贴到地址栏并按 Enter。页面应重新加载并重定向到指定的 URL。

<a id="deploy-extra-services"></a>

## 部署额外服务

初始部署仅部署了托管 URL 缩短应用所需的最少服务。要使用 Azure 数据服务实现 Grain 持久化，必须先配置模板以部署所选服务。

:::zone pivot="azure-storage"

1. 在终端中运行 `azd env set`，配置 `DEPLOY_AZURE_TABLE_STORAGE` 环境变量以启用 Azure Table Storage 部署。

   ```azuredeveloper
   azd env set DEPLOY_AZURE_TABLE_STORAGE true
   ```

:::zone-end

:::zone pivot="azure-cosmos-db-nosql"

1. 在终端中运行 `azd env set`，配置 `DEPLOY_AZURE_COSMOS_DB_NOSQL` 环境变量以启用 Azure Cosmos DB for NoSQL 部署。

   ```azuredeveloper
   azd env set DEPLOY_AZURE_COSMOS_DB_NOSQL true
   ```

:::zone-end

1. 运行 `azd provision`，使用新配置重新部署应用架构。等待预配过程完成。该过程可能需要**大约两分钟**。

   ```azuredeveloper
   azd provision
   ```

   > [!TIP]
   > 或者，可以再次运行 `azd up`，它将同时预配架构并重新部署应用。

<a id="install-nuget-packages"></a>

## 安装 NuGet 包

添加共享提供程序之前，使模板与代码片段保持一致：

1. 将工作目录切换到 _./src/web/_。

   ```bash
   cd ./src/web
   ```

1. 在项目文件中，将 `TargetFramework` 更改为 `net10.0`。

1. 在 _./src/web/Dockerfile_ 中更新两个 [.NET 容器映像](https://learn.microsoft.com/dotnet/core/docker/container-images)：构建阶段使用 `mcr.microsoft.com/dotnet/sdk:10.0`，运行时阶段使用 `mcr.microsoft.com/dotnet/aspnet:10.0`。

1. 升级 Orleans 宿主包：

   ```dotnetcli
   dotnet package add Microsoft.Orleans.Server --version 10.2.2
   ```

接下来安装对应的 `Microsoft.Orleans.Clustering.*` 和 `Microsoft.Orleans.Persistence.*` NuGet 包。这些服务使用基于角色的访问控制进行无密码身份验证，因此还必须导入 `Azure.Identity` NuGet 包。

:::zone pivot="azure-storage"

1. 从 NuGet 导入 `Azure.Identity` 包：

   ```dotnetcli
   dotnet package add Azure.Identity
   ```

1. 导入 `Microsoft.Orleans.Clustering.AzureStorage` 和 `Microsoft.Orleans.Persistence.AzureStorage` 包。

   | 功能           | NuGet 包                                     |
   |-----------------|----------------------------------------------|
   | **集群成员资格** | `Microsoft.Orleans.Clustering.AzureStorage`  |
   | **持久化**      | `Microsoft.Orleans.Persistence.AzureStorage` |

   ```dotnetcli
   dotnet package add Microsoft.Orleans.Clustering.AzureStorage --version 10.2.2
   dotnet package add Microsoft.Orleans.Persistence.AzureStorage --version 10.2.2
   ```

:::zone-end

:::zone pivot="azure-cosmos-db-nosql"

1. 从 NuGet 导入 `Azure.Identity` 包：

   ```dotnetcli
   dotnet package add Azure.Identity
   ```

1. 导入 `Microsoft.Orleans.Clustering.Cosmos` 和 `Microsoft.Orleans.Persistence.Cosmos` 包。

   | 功能           | NuGet 包                               |
   |-----------------|----------------------------------------|
   | **集群成员资格** | `Microsoft.Orleans.Clustering.Cosmos`  |
   | **持久化**      | `Microsoft.Orleans.Persistence.Cosmos` |

   ```dotnetcli
   dotnet package add Microsoft.Orleans.Clustering.Cosmos --version 10.2.2
   dotnet package add Microsoft.Orleans.Persistence.Cosmos --version 10.2.2
   ```

:::zone-end

<a id="configure-and-redeploy-the-sample-app"></a>

## 配置并重新部署示例应用

示例应用最初创建本地主机集群，并将 Grain 状态保存在内存中。在 Azure 中托管时，Orleans 可以使用共享数据服务存储集群成员资格及持久化 Grain 状态。

1. 在 _src/web/Program.cs_ 文件中找到并移除现有的 `builder` 配置代码。

   :::code source="snippets/url-shortener/orleansurlshortener/Program.cs" id="orleans-configuration":::

:::zone pivot="azure-storage"

1. 添加所需的 `using` 指令：

   :::code source="snippets/deploy-scale-orleans-on-azure/azure-storage/Program.cs" id="azure-storage-usings":::

1. 使用以下示例替换 `builder` 配置，其中实现了这些关键概念：

   - 添加条件环境检查，确保应用在本地开发和 Azure 托管场景中均可正常运行。
   - <xref:Orleans.Hosting.AzureTableClusteringExtensions.UseAzureStorageClustering*?displayProperty=nameWithType> 将 Orleans 集群配置为使用 Azure Table Storage，并通过 <xref:Azure.Identity.DefaultAzureCredential> 进行身份验证。
   - 配置设置 <xref:Orleans.Configuration.ClusterOptions.ClusterId> 和 <xref:Orleans.Configuration.ClusterOptions.ServiceId>。
   - <xref:Orleans.Configuration.ClusterOptions.ClusterId> 标识集群，使其客户端与 Silo 可以通信。必须保持隔离的部署应使用不同值。
   - <xref:Orleans.Configuration.ClusterOptions.ServiceId> 标识应用，应跨部署保持一致。

    :::code source="snippets/deploy-scale-orleans-on-azure/azure-storage/Program.cs" id="azure-storage-configuration":::

:::zone-end

:::zone pivot="azure-cosmos-db-nosql"

1. 添加所需的 `using` 指令：

   :::code source="snippets/deploy-scale-orleans-on-azure/cosmos/Program.cs" id="cosmos-usings":::

1. 使用以下示例替换 `builder` 配置，其中实现了这些关键概念：

   - 添加条件环境检查，确保应用在本地开发和 Azure 托管场景中均可正常运行。
   - <xref:Orleans.Hosting.HostingExtensions.UseCosmosClustering*?displayProperty=nameWithType> 将 Orleans 集群配置为使用 Azure Cosmos DB for NoSQL，并通过 <xref:Azure.Identity.DefaultAzureCredential> 进行身份验证。
   - 配置设置 <xref:Orleans.Configuration.ClusterOptions.ClusterId> 和 <xref:Orleans.Configuration.ClusterOptions.ServiceId>。
   - <xref:Orleans.Configuration.ClusterOptions.ClusterId> 标识集群，使其客户端与 Silo 可以通信。必须保持隔离的部署应使用不同值。
   - <xref:Orleans.Configuration.ClusterOptions.ServiceId> 标识应用，应跨部署保持一致。

   :::code source="snippets/deploy-scale-orleans-on-azure/cosmos/Program.cs" id="cosmos-configuration":::

:::zone-end

1. 运行 `azd deploy`，将应用代码作为 Docker 容器重新部署。等待部署过程完成。该过程可能需要**大约一分钟**。

   ```azuredeveloper
   azd deploy
   ```

   > [!TIP]
   > 或者，可以再次运行 `azd up`，它将同时预配架构并重新部署应用。

<a id="verify-the-apps-behavior"></a>

## 验证应用行为

再次使用已部署的应用并检查其数据存储位置，以验证更新后的代码是否正常工作。

1. 在浏览器地址栏中添加 URL 路径，例如 `/shorten?url=https://learn.microsoft.com/dotnet/orleans`，再次测试 `shorten` 终结点。页面应重新加载并提供一个末尾带有缩短路径的新 URL。将新 URL 复制到剪贴板。

   ```output
   {
     "original": "https://learn.microsoft.com/dotnet/orleans",
    "shortened": "http://<container-app-name>.<deployment-name>.<region>.azurecontainerapps.io:<port>/go/<generated-id>"
   }
   ```

1. 将缩短后的 URL 粘贴到地址栏并按 Enter。页面应重新加载并重定向到指定的 URL。

也可以验证集群数据和状态数据是否按预期存储在已创建的存储账户中。

1. 在 Azure 门户中导航到本快速入门部署的资源组。

   > [!IMPORTANT]
   > 本快速入门前面指定的环境名称也是目标资源组名称。

:::zone pivot="azure-storage"

1. 导航到 Azure Storage 账户的概述页面。

1. 在导航栏中选择 **Storage browser**（存储浏览器）。

1. 展开 **Tables**（表）导航项，查看 Orleans 创建的两个表：

   - **OrleansGrainState**：此表存储应用用于处理 URL 重定向的持久化 Grain 状态数据。
   - **OrleansSiloInstances**：此表跟踪 Orleans 集群的重要 Silo 数据。

1. 选择 **OrleansGrainState** 表。测试过程中应用持久化的每个 URL 重定向在该表中均有一行记录。

   :::image type="content" source="media/deploy-scale-orleans-on-azure/storage-table-entities.png" alt-text="Azure 表存储中保存的 Orleans 数据。":::

:::zone-end

:::zone pivot="azure-cosmos-db-nosql"

1. 导航到 Azure Cosmos DB for NoSQL 账户的概述页面。

1. 在导航栏中选择 **Data Explorer**（数据资源管理器）。

1. 查看本指南前面创建的以下容器：

   - **OrleansStorage**：此表存储应用用于处理 URL 重定向的持久化 Grain 状态数据。

   - **OrleansCluster**：此表跟踪 Orleans 集群的重要 Silo 数据。

:::zone-end

<a id="scale-the-app"></a>

## 扩缩应用

不要增加本快速入门创建的单 Silo Container App 的副本数。Orleans 要求每个 Silo 通告一对唯一且可直接到达的终结点，而 Azure Container Apps 不为同一个应用中的各副本提供稳定的独立地址。

要扩展此拓扑，请部署更多各自仅含一个副本、具有不同通告终结点的 Silo 应用。有关受支持拓扑、网络要求及升级流程，请参阅[将 Orleans 部署到 Azure Container Apps](../deployment/deploy-to-azure-container-apps.md)。

<a id="related-content"></a>

## 相关内容

:::zone pivot="azure-storage"

- [Azure Table Storage](https://learn.microsoft.com/azure/storage/tables/table-storage-overview)
- [Azure Storage Grain 持久化](../grains/grain-persistence/azure-storage.md)

:::zone-end

:::zone pivot="azure-cosmos-db-nosql"

- [Azure Cosmos DB for NoSQL](https://learn.microsoft.com/azure/cosmos-db/nosql/overview)
- [Azure Cosmos DB Grain 持久化](../grains/grain-persistence/azure-cosmos-db.md)

:::zone-end

<a id="next-step"></a>

## 下一步

> [!div class="nextstepaction"]
> [Grain 持久化](../grains/grain-persistence/index.md)
