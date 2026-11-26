---
title: "教学: 构建您的第一个 Orleans 应用程序"
description: "构建多项目 Orleans 具有 a 和 a 项 silo 和外部客户。"
ms.date: 08/18/2026
ms.topic: tutorial
ms.devlang: csharp
---

<a id="tutorial-build-your-first-orleans-app"></a>

# 教学: 构建您的第一个 Orleans 应用程序

在此教程中,您使用大应用程序通常使用的项目边界建立一个小型 Orleans 应用程序。 Grain 合同、 grain 执行、 silo和外部客户是具有不同依赖性和部署作用的单独项目。

你学会如何:

- 定义并落实 grain。
- 东道主主 grains a 中 silo.
- 连接外部客户端到 silo。
- 获得 grain 参考并调用。
- 结构项目参考,使客户依赖合同而不是执行。

手动通路使用本地主机群集和瞬时的模拟状态,使 silo 和客户可以直接从两个终端运行。

<a id="prerequisites"></a>

## 先决条件

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- 编辑器, 如 [视觉摄影棚](https://visualstudio.microsoft.com/) 或 [视觉工作室代码](https://code.visualstudio.com/)
- 两个终端,使 silo 和客户端能够同时运行

<a id="create-the-solution"></a>

## 创建解决方案

创建四个 Orleans 项目的解决办法:

- **GrainInterfaces** 含有由打电话者和执行方共同分担的 grain 合同。
- **Grains** 含有 grain 履行情况。
- **Silo** 主机是 Orleans 运行时间和 grain 启动。
- **客户** 是一个外部程序,与 silo 连接并呼叫 grains。

`Microsoft.Orleans.Templates` 软件包创建了相同的 Orleans 项目边界,并添加了一个 Aspire AppHost。生成的应用使用Azurite 支持的 Azure 表群和 Azure Blob grain 存储,而不是手动行走(knowlep) 的本地主机配置。运行中的容器运行时间让 AppHost 启动 Azurite:

```dotnetcli
dotnet new install Microsoft.Orleans.Templates
dotnet new orleans --name OrleansHelloWorld --output OrleansHelloWorld
```

继续下面的手工步骤,学习项目和参考材料如何结合。

在空目录中运行下列命令 :

```dotnetcli
dotnet new sln --name OrleansHelloWorld --format slnx
dotnet new classlib --name GrainInterfaces --framework net10.0
dotnet new classlib --name Grains --framework net10.0
dotnet new console --name Silo --framework net10.0
dotnet new console --name Client --framework net10.0

dotnet solution OrleansHelloWorld.slnx add GrainInterfaces/GrainInterfaces.csproj
dotnet solution OrleansHelloWorld.slnx add Grains/Grains.csproj
dotnet solution OrleansHelloWorld.slnx add Silo/Silo.csproj
dotnet solution OrleansHelloWorld.slnx add Client/Client.csproj

dotnet reference add GrainInterfaces/GrainInterfaces.csproj --project Grains/Grains.csproj
dotnet reference add Grains/Grains.csproj --project Silo/Silo.csproj
dotnet reference add GrainInterfaces/GrainInterfaces.csproj --project Client/Client.csproj

dotnet package add Microsoft.Orleans.Sdk --version 10.2.2 --project GrainInterfaces/GrainInterfaces.csproj
dotnet package add Microsoft.Orleans.Sdk --version 10.2.2 --project Grains/Grains.csproj
dotnet package add Microsoft.Orleans.Server --version 10.2.2 --project Silo/Silo.csproj
dotnet package add Microsoft.Orleans.Client --version 10.2.2 --project Client/Client.csproj
dotnet package add Microsoft.Extensions.Hosting --version 10.0.9 --project Silo/Silo.csproj
dotnet package add Microsoft.Extensions.Hosting --version 10.0.9 --project Client/Client.csproj
```

客户端仅引用 **GrainInterfaces**。它不需要 grain 执行组装。 silo 参考 **Grains**,转而引用 **GrainInterfaces**。

<a id="define-the-grain-contract"></a>

## 定义 grain 合同

删除 _Grain 界面/Class1.cs_,创建_Grain 接口/IHello.cs_,并添加以下 grain 接口:

:::code source="snippets/hello-world/GrainInterfaces/IHello.cs" id="grain-interface":::

<xref:Orleans.IGrainWithStringKey> identifies the grain by a string key. Grain contracts use asynchronous return types because calls can cross process and network boundaries.

<a id="implement-the-grain"></a>

## 执行 grain

删除 _Granins/Class1.cs_,创建_Grans/HelloGrain.cs_,并添加以下执行内容:

:::code source="snippets/hello-world/Grains/HelloGrain.cs" id="grain-implementation":::

实施从 <xref:Orleans.Grain> 继承并落实 `IHello`。 Orleans 源生成器在建设时间发现 grain 合同并履行,所以不需要人工登记申请部分。

<a id="configure-the-silo"></a>

## 配置 silo

将 _SILO/Program.cs_ 改为以下编码:

:::code source="snippets/hello-world/Silo/Program.cs" id="first_orleans_app_silo_program":::

<xref:Microsoft.Extensions.Hosting.OrleansSiloGenericHostExtensions.UseOrleans*> 将 Orleans silo 添加到 [.NET 通用主机](https://learn.microsoft.com/dotnet/core/extensions/generic-host)。 <xref:Orleans.Hosting.CoreHostingExtensions.UseLocalhostClustering*> 配置仅开发的组合和本地机器上的网关端点。

silo 项目参考 **Grains**,因此运行时间可以发现并激活 `HelloGrain`。

<a id="configure-the-external-client"></a>

## 配置外部客户端

将 _Client/Program.cs_ 改为以下编码:

:::code source="snippets/hello-world/Client/Program.cs" id="first_orleans_app_client_program":::

<xref:Microsoft.Extensions.Hosting.OrleansClientGenericHostExtensions.UseOrleansClient*> 向通用主机添加一个外部 Orleans 客户端。客户端使用与 silo相同的本地主机群集配置。在主机启动后,客户端通过自控注射解决 <xref:Orleans.IGrainFactory> ,获得 grain 参考,并援引 grain。

<a id="build-and-run-the-application"></a>

## 构建并运行应用程序

构建解决方案 :

```dotnetcli
dotnet build OrleansHelloWorld.slnx
```

在第一个终端中启动 silo :

```dotnetcli
dotnet run --project Silo
```

等到 silo 打印 `Application started`之后,然后在第二个终端启动客户端:

```dotnetcli
dotnet run --project Client
```

客户打印了 grain 答复:

```output
Hello, Hi friend!
```

客户端从不直接创建或定位 `HelloGrain` 对象。 `GetGrain<IHello>("friend")` 返回逻辑引用。当客户点援引 `SayHello`时, Orleans 将呼叫通过一个 silo 网关,如果它尚未激活,则启动 grain 。

停下来 silo 以按 <kbd>Ctrl 电转</kbd>+<kbd>C</kbd> 在其终端中。

<a id="next-steps"></a>

## 后续步骤

- [在共同主机和外部客户端中选择](../host/client.md)
- [用于生产的配置 Orleans](../host/configuration-guide/index.md)
- [更多了解 grain 身份](../grains/grain-identity.md)
- [浏览浏览抽样](../tutorials-and-samples/index.md)
