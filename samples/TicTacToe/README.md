---
languages:
- csharp
products:
- dotnet
- dotnet-orleans
page_type: sample
name: "Orleans TicTacToe web-based game"
urlFragment: "orleans-tictactoe-web-based-game"
description: "网基游戏 Orleans 样本 TicTacToe"
---

<a id="orleans-tictactoe-web-based-game"></a>

# Orleans TicTacToe 网基游戏

本样本展示了基于网络的 [Tic-tac-toe](https://en.wikipedia.org/wiki/Tic-tac-toe) 游戏。

游戏作为单一项目执行, `TicTacToe.csproj`,该 [.NET 通用主机](https://docs.microsoft.com/dotnet/core/extensions/generic-host) 将 ASP.NET 核心MVC应用软件与 Orleans.

游戏的客户端是用于测试 MVC 更新应用程序的 JavaScript 应用程序。 MVC 控制器将请求转发到 grains。 应用程序有三种 grains:

* `PlayerGrain` 代表一个玩家, 让调用者可以加入和退出游戏, 更新属性, 并检索过去和现在游戏的概览 。
* `GameGrain` 代表单个游戏会话和相应的游戏逻辑。 `GameGrain` 允许客户进行移动并查看当前游戏板状态。
* `PairingGrain` ,它持有一份其他玩家可以加入的现有游戏列表。

电话流量如下:

![显示应用程序中通话的图表](dataflow.png)

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

打开终端窗口,并在命令提示时执行以下内容:

```bash
dotnet run
```

游戏服务器将启动, 您可以打开一个浏览器到 `https://localhost:5000/` 与游戏互动 。

如果您愿意, 您可以开始更多关于主机的实例来查看它们组成一个集。 如果您可以这样做, 请在命令行中添加 `InstanceId` 选项来区分它们。 生产应用程序将使用除此应用程序所使用的“ 本地主机组合” 以外的其它东西( 参见 _Program.cs_ , 因为组合是通过 `UseLocalhostClustering`配置的) , 因此不需要这个 `InstanceId` 选项 。

```bash
dotnet run -- --InstanceId 1
```

由于游戏使用 cookie 来识别玩家, 您需要另外的浏览器会话才能与自己相对打并体验游戏 。
