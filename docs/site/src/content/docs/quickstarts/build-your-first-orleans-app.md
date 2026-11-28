---
title: '教程：构建第一个 Orleans 应用'
description: 构建包含 Silo 和外部客户端的多项目 Orleans 应用。
ms.date: 08/18/2026
ms.topic: tutorial
ms.devlang: csharp
---

<a id="tutorial-build-your-first-orleans-app"></a>

# 教程：构建第一个 Orleans 应用

本教程将构建一个小型 Orleans 应用，采用大型应用常用的项目划分方式。Grain 契约、Grain 实现、Silo 和外部客户端分别位于独立项目中，具有不同的依赖关系和部署职责。

你将学习如何：

- 定义并实现 Grain。
- 在 Silo 中托管 Grain。
- 将外部客户端连接到 Silo。
- 获取 Grain 引用并调用它。
- 组织项目引用，使客户端依赖契约而非实现。

本教程的手动操作步骤使用本地主机集群和临时内存状态，因此可直接在两个终端中分别运行 Silo 和客户端。

<a id="prerequisites"></a>

## 先决条件

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Visual Studio](https://visualstudio.microsoft.com/) 或 [Visual Studio Code](https://code.visualstudio.com/) 等编辑器
- 两个终端，以便同时运行 Silo 和客户端

<a id="create-the-solution"></a>

## 创建解决方案

创建包含以下四个 Orleans 项目的解决方案：

- **GrainInterfaces** 包含调用方和实现方共享的 Grain 契约。
- **Grains** 包含 Grain 实现。
- **Silo** 托管 Orleans 运行时及 Grain 激活实例。
- **Client** 是连接到 Silo 并调用 Grain 的外部进程。

`Microsoft.Orleans.Templates` 包会创建具有相同项目边界的 Orleans 应用，并添加 Aspire AppHost。生成的应用使用由 Azurite 支持的 Azure Table 集群成员资格和 Azure Blob Grain 存储，而非手动教程中的本地主机配置。运行中的容器运行时使 AppHost 能够启动 Azurite：

```dotnetcli
dotnet new install Microsoft.Orleans.Templates
dotnet new orleans --name OrleansHelloWorld --output OrleansHelloWorld
```

继续按照以下手动步骤操作，了解各项目及其引用关系。

在空目录中运行以下命令：

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

客户端仅引用 **GrainInterfaces**，不需要 Grain 实现程序集。Silo 引用 **Grains**，后者再引用 **GrainInterfaces**。

<a id="define-the-grain-contract"></a>

## 定义 Grain 契约

删除 _GrainInterfaces/Class1.cs_，创建 _GrainInterfaces/IHello.cs_，并添加以下 Grain 接口：

:::code source="snippets/hello-world/GrainInterfaces/IHello.cs" id="grain-interface":::

<xref:Orleans.IGrainWithStringKey> 使用字符串键标识 Grain。由于调用可能跨越进程和网络边界，Grain 契约使用异步返回类型。

<a id="implement-the-grain"></a>

## 实现 Grain

删除 _Grains/Class1.cs_，创建 _Grains/HelloGrain.cs_，并添加以下实现：

:::code source="snippets/hello-world/Grains/HelloGrain.cs" id="grain-implementation":::

该实现继承 <xref:Orleans.Grain> 并实现 `IHello`。Orleans 源代码生成器在构建时发现 Grain 契约和实现，因此无需手动注册应用程序部件。

<a id="configure-the-silo"></a>

## 配置 Silo

将 _Silo/Program.cs_ 替换为以下代码：

:::code source="snippets/hello-world/Silo/Program.cs" id="first_orleans_app_silo_program":::

<xref:Microsoft.Extensions.Hosting.OrleansSiloGenericHostExtensions.UseOrleans*> 将 Orleans Silo 添加到 [.NET 通用主机](https://learn.microsoft.com/dotnet/core/extensions/generic-host)。<xref:Orleans.Hosting.CoreHostingExtensions.UseLocalhostClustering*> 在本机配置仅供开发使用的集群和网关终结点。

Silo 项目引用 **Grains**，因此运行时可以发现并激活 `HelloGrain`。

<a id="configure-the-external-client"></a>

## 配置外部客户端

将 _Client/Program.cs_ 替换为以下代码：

:::code source="snippets/hello-world/Client/Program.cs" id="first_orleans_app_client_program":::

<xref:Microsoft.Extensions.Hosting.OrleansClientGenericHostExtensions.UseOrleansClient*> 将外部 Orleans 客户端添加到通用主机。客户端使用与 Silo 相同的本地主机集群配置。主机启动后，客户端从依赖注入容器解析 <xref:Orleans.IGrainFactory>，获取 Grain 引用并调用 Grain。

<a id="build-and-run-the-application"></a>

## 构建并运行应用

构建解决方案：

```dotnetcli
dotnet build OrleansHelloWorld.slnx
```

在第一个终端中启动 Silo：

```dotnetcli
dotnet run --project Silo
```

等到 Silo 输出 `Application started`，再在第二个终端中启动客户端：

```dotnetcli
dotnet run --project Client
```

客户端输出 Grain 的响应：

```output
Hello, Hi friend!
```

客户端从不直接创建或定位 `HelloGrain` 对象。`GetGrain<IHello>("friend")` 返回逻辑引用。客户端调用 `SayHello` 时，Orleans 会通过 Silo 网关路由该调用，并在 Grain 未处于活动状态时将其激活。

在 Silo 所在终端中按 <kbd>Ctrl</kbd>+<kbd>C</kbd> 停止 Silo。

<a id="next-steps"></a>

## 后续步骤

- [选择共同托管客户端或外部客户端](../host/client.md)
- [配置生产环境中的 Orleans](../host/configuration-guide/index.md)
- [深入了解 Grain 标识](../grains/grain-identity.md)
- [浏览示例](../tutorials-and-samples/index.md)
