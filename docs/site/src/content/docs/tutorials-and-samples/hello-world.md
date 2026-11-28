---
title: '快速入门：Orleans Hello World'
description: 生成并运行最精简的 Orleans 应用程序。
ms.date: 08/08/2026
ms.topic: quickstart
ms.devlang: csharp
---

<a id="quickstart-orleans-hello-world"></a>

# 快速入门：Orleans Hello World

本快速入门在单个项目中构建最精简的 Orleans 应用程序。该进程托管一个 Silo，并使用 Orleans 在每个 Silo 中提供的客户端调用 Grain。

<a id="prerequisites"></a>

## 先决条件

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Visual Studio](https://visualstudio.microsoft.com/) 或 [Visual Studio Code](https://code.visualstudio.com/) 等编辑器

<a id="create-the-project"></a>

## 创建项目

在空目录中运行以下命令：

```dotnetcli
dotnet new console --name HelloWorld --framework net10.0
cd HelloWorld
dotnet package add Microsoft.Orleans.Server --version 10.2.2
dotnet package add Microsoft.Extensions.Hosting --version 10.0.9
```

[`Microsoft.Orleans.Server`](https://www.nuget.org/packages/Microsoft.Orleans.Server) 包含 Orleans 运行时、客户端 API 和 SDK 生成工具。

<a id="define-the-grain-contract"></a>

## 定义 Grain 契约

创建 _IHello.cs_ 并定义 Grain 接口：

:::code source="snippets/hello-world/IHello.cs" id="hello_world_grain_interface":::

<xref:Orleans.IGrainWithStringKey> 使用字符串键标识 Grain。Grain 契约使用异步返回类型，因为调用可能跨越进程和网络边界。

<a id="implement-the-grain"></a>

## 实现 Grain

创建 _HelloGrain.cs_ 并实现 Grain 接口：

:::code source="snippets/hello-world/HelloGrain.cs" id="hello_world_grain":::

<a id="configure-orleans-and-call-the-grain"></a>

## 配置 Orleans 并调用 Grain

将 _Program.cs_ 替换为以下代码：

:::code source="snippets/hello-world/Program.cs" id="hello_world_program":::

<xref:Microsoft.Extensions.Hosting.OrleansSiloGenericHostExtensions.UseOrleans*> 将一个 Silo 及其进程内客户端添加到 [.NET 通用主机](https://learn.microsoft.com/dotnet/core/extensions/generic-host) 中。<xref:Orleans.Hosting.CoreHostingExtensions.UseLocalhostClustering*> 在本机配置仅供开发使用的集群。

主机启动后，从依赖注入容器解析 <xref:Orleans.IGrainFactory>，获取由 `friend` 标识的 Grain 的逻辑引用，然后调用它。

<a id="run-the-app"></a>

## 运行应用程序

```dotnetcli
dotnet run
```

应用程序输出：

```output
Hello, Hi friend!
```

<a id="next-steps"></a>

## 后续步骤

- 使用典型的多项目结构[构建你的第一个 Orleans 应用程序](../quickstarts/build-your-first-orleans-app.md)。
- [了解 Orleans 概念](../overview.md)。
- [浏览示例](index.md)。
