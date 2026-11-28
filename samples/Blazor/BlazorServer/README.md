---
languages:
- csharp
products:
- dotnet
- dotnet-orleans
page_type: sample
name: "Orleans ASP.NET Core Blazor Server 示例"
urlFragment: "orleans-aspnet-core-blazor-server-sample"
description: "使用 Blazor 和 ASP.NET Core 的 Orleans 示例。"
---

<a id="orleans-aspnet-core-blazor-server-sample"></a>

# Orleans ASP.NET Core Blazor Server 示例

本示例演示如何将 [ASP.NET Core Blazor](https://docs.microsoft.com/aspnet/core/blazor/)
与 [Microsoft Orleans](https://docs.microsoft.com/dotnet/orleans/) 集成。
本示例演示浏览器中的 [Blazor Server 托管模型](https://docs.microsoft.com/aspnet/core/blazor/hosting-models#blazor-server)。

应用程序利用 Orleans Streams 在浏览器会话之间提供实时同步。

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

打开终端窗口，在命令提示符下执行以下命令以运行示例：

```dotnetcli
dotnet run
```

应用程序启动后，可通过 Web 浏览器访问 [http://localhost:5000/](http://localhost:5000/)。

在多个浏览器会话中打开该地址，可以在*待办事项列表*演示中观察浏览器会话之间的实时同步。

<a id="demos"></a>

## 演示

客户端和服务器端应用均展示相同的三个演示：

<a id="counter"></a>

### 计数器

显示一个用于更新计数器的按钮。
这演示了 Blazor 中的基本逻辑。
此演示不与 Orleans 集成。

<a id="fetch-data"></a>

### 获取数据

从 Orleans 获取只读数据的页面。

应用程序通过 [`WeatherForecastService`](./Services/WeatherForecastService.cs) 类型发起 Grain 调用，从 Orleans Grain 获取数据。

<a id="todo-list"></a>

### 待办事项列表

用于管理待办事项列表的页面。
允许创建、编辑及删除待办事项。

应用程序通过直接调用 Orleans 集群中提供支持的 Grain 管理这些数据。
应用程序通过 Orleans Streams 订阅此列表中的各项变更。
这使应用程序能够在其他浏览器会话发生变更时实时更新待办事项列表。
变更通过 Blazor 底层的 SignalR 基础设施实时呈现并发送至浏览器。

要演示实时服务器更新，请打开多个显示服务器端待办事项演示的浏览器窗口，
然后在任意窗口中修改待办事项列表。其他窗口会实时反映该更新。

![屏幕截图](screenshot.png)
