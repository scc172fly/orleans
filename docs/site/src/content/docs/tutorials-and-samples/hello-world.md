---
title: "快速启动: Orleans 你好世界"
description: "构建并运行最起码的 Orleans 应用程序。"
ms.date: 08/08/2026
ms.topic: quickstart
ms.devlang: csharp
---

<a id="quickstart-orleans-hello-world"></a>

# 快速启动: Orleans 你好世界

快速启动的最小 Orleans 在一个项目应用程序中。该程序主机 silo 和调用a grain 使用客户端 Orleans 内提供 silo.

<a id="prerequisites"></a>

## 先决条件

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- 编辑器, 如 [视觉摄影棚](https://visualstudio.microsoft.com/) 或 [视觉工作室代码](https://code.visualstudio.com/)

<a id="create-the-project"></a>

## 创建工程

在空目录中运行下列命令 :

```dotnetcli
dotnet new console --name HelloWorld --framework net10.0
cd HelloWorld
dotnet package add Microsoft.Orleans.Server --version 10.2.2
dotnet package add Microsoft.Extensions.Hosting --version 10.0.9
```

[`Microsoft.Orleans.Server`](https://www.nuget.org/packages/Microsoft.Orleans.Server) 包括 Orleans 运行时间、客户APIs和SDK 建设工具。

<a id="define-the-grain-contract"></a>

## 定义 grain 合同

创建 _IHello.cs_ 并定义一个 grain 接口:

:::code source="snippets/hello-world/IHello.cs" id="hello_world_grain_interface":::

<xref:Orleans.IGrainWithStringKey> identifies the grain by a string key. Grain contracts use asynchronous return types because calls can cross process and network boundaries.

<a id="implement-the-grain"></a>

## 执行 grain

创建 _HelloGrain.cs_ 并安装 grain 接口:

:::code source="snippets/hello-world/HelloGrain.cs" id="hello_world_grain":::

<a id="configure-orleans-and-call-the-grain"></a>

## 配置 Orleans 并调用 grain

将 _Program.cs_ 改为以下编码:

:::code source="snippets/hello-world/Program.cs" id="hello_world_program":::

<xref:Microsoft.Extensions.Hosting.OrleansSiloGenericHostExtensions.UseOrleans*> 加a silo 及其在程序中的客户端 [.NET 通用主机](https://learn.microsoft.com/dotnet/core/extensions/generic-host). <xref:Orleans.Hosting.CoreHostingExtensions.UseLocalhostClustering*> 配置本地机器上仅开发的组合。

东道主启动后, 决心 <xref:Orleans.IGrainFactory>,获取逻辑引用 grain 由 `friend`,并称它。

<a id="run-the-app"></a>

## 运行应用程序

```dotnetcli
dotnet run
```

应用程序指纹:

```output
Hello, Hi friend!
```

<a id="next-steps"></a>

## 后续步骤

- 使用典型的多项目结构[构建您的第一个 Orleans 应用程序](../quickstarts/build-your-first-orleans-app.md) 。
- [理解 Orleans 概念](../overview.md).
- [浏览浏览抽样](index.md).
