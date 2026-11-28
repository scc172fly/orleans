---
languages:
- csharp
products:
- dotnet
- dotnet-orleans
page_type: sample
name: "Orleans ASP.NET Core Blazor WebAssembly 示例"
urlFragment: "orleans-aspnet-core-blazor-wasm-sample"
description: "使用 Blazor 和 ASP.NET Core 的 Orleans 示例。"
---

<a id="orleans-aspnet-core-blazor-webassembly-sample"></a>

# Orleans ASP.NET Core Blazor WebAssembly 示例

本示例演示如何将 [ASP.NET Core Blazor](https://docs.microsoft.com/aspnet/core/blazor/)
与 [Microsoft Orleans](https://docs.microsoft.com/dotnet/orleans/) 集成。这演示了浏览器中的 [Blazor WebAssembly 托管模型](https://docs.microsoft.com/aspnet/core/blazor/hosting-models#blazor-webassembly)。

客户端示例应用使用与 Orleans 一起运行的 ASP.NET Web API 进行标准 Web 通信。

应用程序基于[官方教程](https://dotnet.microsoft.com/learn/aspnet/blazor-tutorial/intro)，并进行了调整以展示与 Orleans 的集成。

<a id="sample-prerequisites"></a>

## 示例先决条件

本示例使用 C# 编写，目标框架为 .NET 10。需要 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 或更高版本。

<a id="building-the-sample"></a>

## 构建示例

按照以下步骤下载并运行示例：

1. 下载并解压示例。
2. 在 Visual Studio（2022 或更高版本）中：
    1. 在菜单栏中选择 **文件** > **打开** > **项目/解决方案**。
    2. 导航到存放已解压示例代码的文件夹，并打开 C# 项目（.csproj）文件。
    3. 按 <kbd>F5</kbd> 调试运行，或按 <kbd>Ctrl</kbd>+<kbd>F5</kbd> 在不调试的情况下运行项目。
3. 从命令行运行：
   1. 导航到存放已解压示例代码的文件夹。
   2. 在命令行输入 [`dotnet run`](https://docs.microsoft.com/dotnet/core/tools/dotnet-run)。

<a id="running-the-sample"></a>

## 运行示例

打开两个终端窗口，在其中一个终端中执行以下命令以运行示例：

```dotnetcli
dotnet run --project BlazorWasm.Server
```

在另一个终端中执行以下命令：

```dotnetcli
dotnet run --project BlazorWasm.Client
```

两个应用程序启动后，可以通过以下地址访问它们：

* 由 BlazorWasm.Server 进程托管的 Swagger UI：[http://localhost:5000/swagger/index.html](http://localhost:5000/swagger/index.html)
* 由 BlazorWasm.Client 进程托管的客户端 Blazor 应用：[http://localhost:62654/](http://localhost:62654/)

<a id="demos"></a>

## 演示

应用程序展示三个演示：

<a id="counter"></a>

### 计数器

显示一个用于更新计数器的按钮。
这演示了 Blazor 中的基本逻辑。
此演示不与 Orleans 集成。

<a id="fetch-data"></a>

### 获取数据

从 Orleans 获取只读数据的页面。
客户端应用从与 Orleans 一起运行的 ASP.NET Core Web API 获取这些数据。

<a id="todo"></a>

### 待办事项

用于管理待办事项列表的页面。
允许创建、编辑及删除待办事项。

客户端应用通过 REST 调用与 Orleans 一起运行的 ASP.NET Core Web API 来管理这些数据。
