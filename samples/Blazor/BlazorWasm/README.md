---
languages:
- csharp
products:
- dotnet
- dotnet-orleans
page_type: sample
name: "Orleans ASP.NET Core Blazor WebAssembly sample"
urlFragment: "orleans-aspnet-core-blazor-wasm-sample"
description: "举例来说 Orleans 使用Blazor和 ASP.NET 核心"
---

<a id="orleans-aspnet-core-blazor-webassembly-sample"></a>

# Orleans ASP.NET 核心闪光器 WebAssembly 样本

本样本演示如何整合 [ASP.NET 核心布拉佐](https://docs.microsoft.com/aspnet/core/blazor/)
与 [Microsoft Orleans](https://docs.microsoft.com/dotnet/orleans/)。此表显示浏览器 [Blazor WebAssembly 主机模式](https://docs.microsoft.com/aspnet/core/blazor/hosting-models#blazor-webassembly)。

客户方抽样应用软件的杠杆 ASP.NET 网 网 API 与 Orleans 用于标准网络通信。

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

运行样本,打开两个终端窗口并在一个终端中执行以下操作:

```dotnetcli
dotnet run --project BlazorWasm.Server
```

在其它终端中执行下列操作 :

```dotnetcli
dotnet run --project BlazorWasm.Client
```

申请一经启动,您可在下列地址获取:

* 由 BlazorWasm.Server 进程主持, [http://localhost:5000/swagger/index.html](http://localhost:5000/swagger/index.html)的 Swagger 界面
* BlazorWasm.Client 程序托管的客户-用户-服务面布拉佐 App: [http://localhost:62654/](http://localhost:62654/)

<a id="demos"></a>

## 演示

应用程序展示了三个演示:

<a id="counter"></a>

### 柜台

显示更新柜台的按钮。
这在布拉佐里表明了基本逻辑。
此演示不与 Orleans融合。

<a id="fetch-data"></a>

### 获取数据

从 Orleans获取只读数据的页面。
用户端应用程序从 ASP.NET 核心网 API 与 Orleans.

<a id="todo"></a>

### 待待办待办

管理待办事宜列表的页面 。
允许创建、编辑和删除待办事宜项目。

用户端应用程序通过REST呼叫管理这些数据 ASP.NET 核心网 API 与 Orleans.
