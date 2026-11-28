---
languages:
- csharp
products:
- dotnet
- dotnet-orleans
page_type: sample
name: "Orleans：购物车应用示例"
urlFragment: "orleans-shopping-cart-app-sample"
description: "使用 Microsoft Orleans 构建的典型购物车应用示例。"
---

<a id="orleans-shopping-cart-app-sample"></a>

# Orleans：购物车应用示例

> [!NOTE]
> 本示例位于 [`dotnet/orleans` 仓库](https://github.com/dotnet/orleans/tree/main/samples/ShoppingCart)，从 [`dotnet/samples`](https://github.com/dotnet/samples/tree/main/orleans/ShoppingCart) 导入。

这是使用 Microsoft Orleans 构建的典型购物车应用示例，展示以下功能：

- **购物车**：利用 Orleans 的跨平台框架支持和可伸缩分布式应用能力实现的简单购物车应用。

  - **库存管理**：编辑和/或创建商品库存。
  - **浏览商品**：浏览可购买的商品并将其加入购物车。
  - **购物车**：查看购物车中所有商品的汇总，并管理这些商品，包括删除商品或更改各商品数量。

![运行中的购物车示例应用。](media/shopping-cart.png)

<a id="features"></a>

## 功能特性

- [.NET 10](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-10/overview)
- [ASP.NET Core Blazor](https://docs.microsoft.com/aspnet/core/blazor)
- [Orleans：Grain 持久化](https://docs.microsoft.com/dotnet/orleans/grains/grain-persistence)
- [Azure Storage Grain 持久化](https://docs.microsoft.com/dotnet/orleans/grains/grain-persistence/azure-storage)
  - [Orleans：集群管理](https://docs.microsoft.com/dotnet/orleans/implementation/cluster-management)
- [Orleans：代码生成](https://docs.microsoft.com/dotnet/orleans/grains/code-generation)
- [Orleans：启动任务](https://docs.microsoft.com/dotnet/orleans/host/configuration-guide/startup-tasks)
- [Azure Bicep](https://docs.microsoft.com/azure/azure-resource-manager/bicep)
- [Azure App Service](https://docs.microsoft.com/azure/app-service/overview)
- [GitHub Actions 与 .NET](https://docs.microsoft.com/dotnet/devops/github-actions-overview)

应用架构如下：

![购物车示例应用架构。](media/shopping-cart-arch.png)

<a id="get-started"></a>

## 入门

<a id="prerequisites"></a>

### 先决条件

- 一个 [GitHub 账户](https://docs.github.com/en/get-started/start-your-journey/creating-an-account-on-github)
- [.NET 10 SDK 或更高版本](https://dotnet.microsoft.com/download/dotnet)
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli)
- .NET 集成开发环境（IDE）
  - 可以使用 [Visual Studio IDE](https://visualstudio.microsoft.com) 或 [Visual Studio Code](https://code.visualstudio.com)

<a id="quickstart"></a>

### 快速入门

1. `git clone https://github.com/dotnet/orleans.git`
1. `cd orleans/samples/ShoppingCart`
1. `dotnet run --project Silo\Orleans.ShoppingCart.Silo.csproj --environment ASPNETCORE_ENVIRONMENT=Development`

<a id="deploying-to-azure"></a>

### 部署到 Azure

部署到 Azure 之前，请确保完成以下步骤：

1. 创建 Azure Cosmos DB for NoSQL 账户。
    1. 创建名为 `Orleans` 的数据库。
    1. 在 `Orleans` 数据库中创建名为 `OrleansStorage` 的容器，分区键路径为 `/PartitionKey`。
    1. 在 `Orleans` 数据库中创建另一个名为 `OrleansCluster` 的容器，确保该容器的分区键路径为 `/ClusterId`。
    1. 获取连接字符串。
1. 创建 Azure Container App 作为部署目标。
    1. 确保入口目标端口为 `8080`。有关详细信息，请参阅[默认 ASP.NET Core 端口已更改为 8080](https://learn.microsoft.com/dotnet/core/compatibility/containers/8.0/aspnet-port)。
    1. 在 Container App 中为 Azure Cosmos DB for NoSQL 账户的连接字符串创建机密。
    1. 在 Container App 的容器中创建名为 `ORLEANS_AZURE_COSMOS_DB_CONNECTION_STRING` 的环境变量，并引用刚创建的机密。
1. 将应用部署到 Azure Container App 服务。有关详细信息，请参阅 [Azure Container Apps 部署选项](https://learn.microsoft.com/azure/container-apps/code-to-cloud-options)。

<a id="build-docker-image-locally"></a>

### 在本地构建 Docker 映像

1. 从 [Docker Hub](https://hub.docker.com/editions/community/docker-ce-desktop-windows) 安装 Docker Desktop。
2. 在当前目录运行 `docker build ../.. -t orleans-shopping-cart -f Silo/Dockerfile`，以仓库根目录作为构建上下文，在本地构建 Docker 映像。

<a id="acknowledgements"></a>

### 致谢

Orleans.ShoppingCart.Silo 项目使用以下第三方开源项目：

- [MudBlazor](https://github.com/MudBlazor/MudBlazor)：基于 Material Design 的 Blazor 组件库。
- [Bogus](https://github.com/bchavez/Bogus)：适用于 C#、F# 和 VB.NET 的简单模拟数据生成器。
- [Blazorators](https://github.com/IEvangelist/blazorators)：用于 Blazor JavaScript 互操作的源代码生成包。

源自 [IEvangelist/orleans-shopping-cart](https://github.com/IEvangelist/orleans-shopping-cart)。

<a id="resources"></a>

## 资源

- [将 Orleans 部署到 Azure App Service](https://aka.ms/orleans-on-app-service)
