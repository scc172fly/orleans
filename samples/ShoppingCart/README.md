---
languages:
- csharp
products:
- dotnet
- dotnet-orleans
page_type: sample
name: "Orleans: Shopping Cart App sample"
urlFragment: "orleans-shopping-cart-app-sample"
description: "使用 Microsoft Orleans建造的卡尼卡购物车样品应用软件。"
---

<a id="orleans-shopping-cart-app-sample"></a>

# Orleans:购物墨盒装饰样本

> [!NOTE]
> 该样本在 [`dotnet/orleans` 仓库](https://github.com/dotnet/orleans/tree/main/samples/ShoppingCart)中存在。它从 [`dotnet/samples`](https://github.com/dotnet/samples/tree/main/orleans/ShoppingCart)进口。

使用 Microsoft Orleans建造的一家卡尼卡购物车样本应用程序。该应用显示以下特征:

- **购物车**:一个简单的购物车应用程序,使用 Orleans 来提供跨平台框架支持及其可缩放的分布应用能力。

  - **清单管理**:编辑和/或创建产品清单。
  - **商店库存**:探索可购买产品并将其添加到您的车内。
  - **墨盒**:查看您手推车中所有项目的概要,并管理这些项目;或者删除或更改每个项目的数量。

![正在运行购物墨盒样本应用程序。](media/shopping-cart.png)

<a id="features"></a>

## 功能

- [.NET 10](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-10/overview)
- [ASP.NET 核心布拉佐](https://docs.microsoft.com/aspnet/core/blazor)
- [Orleans: Grain 持久性](https://docs.microsoft.com/dotnet/orleans/grains/grain-persistence)
- [Azure 储存 grain 持久性](https://docs.microsoft.com/dotnet/orleans/grains/grain-persistence/azure-storage)
  - [Orleans: 集束管理](https://docs.microsoft.com/dotnet/orleans/implementation/cluster-management)
- [Orleans:代码生成](https://docs.microsoft.com/dotnet/orleans/grains/code-generation)
- [Orleans:启动任务](https://docs.microsoft.com/dotnet/orleans/host/configuration-guide/startup-tasks)
- [Azure 双尖](https://docs.microsoft.com/azure/azure-resource-manager/bicep)
- [Azure 服务处](https://docs.microsoft.com/azure/app-service/overview)
- [GitHub 行动和 .NET](https://docs.microsoft.com/dotnet/devops/github-actions-overview)

该应用程序的设计如下:

![购物墨盒样本应用建筑](media/shopping-cart-arch.png)

<a id="get-started"></a>

## 开始

<a id="prerequisites"></a>

### 先决条件

- A [GitHub 账户](https://docs.github.com/en/get-started/start-your-journey/creating-an-account-on-github)
- 下 [.NET 10 SDK或以后](https://dotnet.microsoft.com/download/dotnet)
- 下 [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli)
- .NET 综合发展环境
  - 使用 [视觉演播室 IDE](https://visualstudio.microsoft.com) 或 [视觉工作室代码](https://code.visualstudio.com)

<a id="quickstart"></a>

### 快速启动

1. `git clone https://github.com/dotnet/orleans.git`
1. `cd orleans/samples/ShoppingCart`
1. `dotnet run --project Silo\Orleans.ShoppingCart.Silo.csproj --environment ASPNETCORE_ENVIRONMENT=Development`

<a id="deploying-to-azure"></a>

### 部署到 Azure

在部署到 Azure之前,确保完成下列步骤:

1. 创建 Azure 宇宙 DB NoSQL 账户。
    1. 创建名为 `Orleans`的数据库。
    1. 在 `Orleans` 数据库中创建 `OrleansStorage` 容器，分区键路径为 `/PartitionKey`。
    1. 在 `Orleans` 数据库中再创建一个 `OrleansCluster` 容器，分区键路径为 `/ClusterId`。
    1. 获取连接字符串。
1. 创建一个 Azure 容器应用程序,作为您部署的目标。
    1. 确保目标进港为 `8080`。更多信息,请参见 [默认缺缺缺 ASP.NET 核心端口改为8080](https://learn.microsoft.com/dotnet/core/compatibility/containers/8.0/aspnet-port)。
    1. 在容器应用中创建机密，保存 Azure Cosmos DB for NoSQL 帐户的连接字符串。
    1. 在容器应用的容器中创建环境变量 `ORLEANS_AZURE_COSMOS_DB_CONNECTION_STRING`，引用刚创建的机密。
1. 将应用程序部署到 Azure 集装箱应用服务。更多信息,请见 [Azure 集装箱集装箱](https://learn.microsoft.com/azure/container-apps/code-to-cloud-options)。

<a id="build-docker-image-locally"></a>

### 本地构建 docker 图像

1. 从 [嵌入器中心](https://hub.docker.com/editions/community/docker-ce-desktop-windows)安装嵌入桌面
2. 从此目录运行 `docker build ../.. -t orleans-shopping-cart -f Silo/Dockerfile` 以仓库根为构建环境, 以本地方式使用存储器根建立 docker 图像

<a id="acknowledgements"></a>

### 致谢

Orleans.ShoppingCart.Silo 项目使用下列开放3缔约方源项目:

- [MudBlazor](https://github.com/MudBlazor/MudBlazor):根据材料设计建立的布拉佐尔组件库。
- [错数](https://github.com/bchavez/Bogus): C#、F#和 VB.NET的简单假数据生成器。
- [爆破器](https://github.com/IEvangelist/blazorators):Blazor JavaScript 切换器源生成的软件包。

取自 [IEvangelist/orleans-shopping-cart](https://github.com/IEvangelist/orleans-shopping-cart)。

<a id="resources"></a>

## 资源资源资源资源资源资源资源 资源资源资源资源资源资源

- [将部署 Orleans 改为 Azure](https://aka.ms/orleans-on-app-service)
