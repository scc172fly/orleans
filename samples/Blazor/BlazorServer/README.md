---
languages:
- csharp
products:
- dotnet
- dotnet-orleans
page_type: sample
name: "Orleans ASP.NET Core Blazor Server sample"
urlFragment: "orleans-aspnet-core-blazor-server-sample"
description: "举例来说 Orleans 使用Blazor和 ASP.NET 核心"
---

<a id="orleans-aspnet-core-blazor-server-sample"></a>

# Orleans ASP.NET 核心Blazor服务器样本

本样本演示如何整合 [ASP.NET 核心布拉佐](https://docs.microsoft.com/aspnet/core/blazor/)
与 [Microsoft Orleans](https://docs.microsoft.com/dotnet/orleans/).
此处显示浏览器 [Blazor 服务器主机模式](https://docs.microsoft.com/aspnet/core/blazor/hosting-models#blazor-server)。

应用程序利用 Orleans 流在浏览器会话之间提供实时同步。

应用程序基于 [官方官方教官](https://dotnet.microsoft.com/learn/aspnet/blazor-tutorial/intro),适合与 Orleans的整合。

<a id="sample-prerequisites"></a>

## 抽样的前提条件

本样本以C##和目标 .NET 10 来写,要求有 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 或以后的。

<a id="building-the-sample"></a>

## 建立样本

下载和运行样本时,请遵循以下步骤:

1. 下载并解开样本的平面
2. 视觉演播室(2022年或以后):
    1. 在菜单栏上,选择 **文件** > **打开** > **项目/解决方案**。
    2. Navigate to the folder that holds the unzipped sample code, and open the C# project (.csproj) file.
    3. Choose the <kbd>F5</kbd> key to run with debugging, or <kbd>Ctrl</kbd>+<kbd>F5</kbd> keys to run the project without debugging.
3. 从命令行 :
   1. 导航到持有未设偏移样本代码的文件夹 。
   2. 在命令行,类型 [`dotnet run`](https://docs.microsoft.com/dotnet/core/tools/dotnet-run)。

<a id="running-the-sample"></a>

## 运行样本

通过打开终端窗口运行样本,并在命令提示处执行以下操作:

```dotnetcli
dotnet run
```

一旦应用程序似乎启动,您可以通过浏览器访问到 [http://localhost:5000/](http://localhost:5000/)。

向该地址打开多个浏览器会话将显示在 *待办列表* 中文本不译。

<a id="demos"></a>

## 演示

客户端和服务器端应用程序展示相同的三个演示:

<a id="counter"></a>

### 柜台

显示更新柜台的按钮。
这在布拉佐里表明了基本逻辑。
此演示不与 Orleans融合。

<a id="fetch-data"></a>

### 获取数据

从 Orleans获取只读数据的页面。

应用程序来源于此数据来自 Orleans grain 生成 grain 通过 [`WeatherForecastService`](./Services/WeatherForecastService.cs) 类型。

<a id="todo-list"></a>

### 待办列表

管理待办事宜列表的页面 。
允许创建、编辑和删除待办事宜项目。

应用程序通过直接呼叫支持来管理这些数据 grains 中在 Orleans 组。
应用程序通过 Orleans 流订阅对列表的个别修改。
这样它就可以在从其他浏览器会话更改后,实时更新待办事宜列表。
修改通过Blazor的基底 SignalR 基础设施实时完成并发送到浏览器。

要显示实时服务器更新, 请打开多个浏览器窗口, 显示服务器端的待办事宜演示,
然后从任何窗口对待办事宜列表进行修改。其他窗口将实时反映更新内容。

![截图](screenshot.png)
