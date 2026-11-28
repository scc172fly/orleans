---
languages:
- csharp
products:
- dotnet
- dotnet-orleans
page_type: sample
name: "Orleans TicTacToe 网页游戏"
urlFragment: "orleans-tictactoe-web-based-game"
description: "Orleans TicTacToe 网页游戏示例。"
---

<a id="orleans-tictactoe-web-based-game"></a>

# Orleans TicTacToe 网页游戏

本示例演示基于 Web 的[井字棋](https://en.wikipedia.org/wiki/Tic-tac-toe)游戏。

游戏在单个项目 `TicTacToe.csproj` 中实现，使用 [.NET 通用主机](https://docs.microsoft.com/dotnet/core/extensions/generic-host) 同时托管 ASP.NET Core MVC 应用与 Orleans。

游戏客户端是 JavaScript 应用，通过轮询 MVC 应用获取更新。MVC 控制器将请求转发给 Grain。应用具有三种 Grain 类型：

* `PlayerGrain` 表示玩家，允许调用方加入和离开游戏、更新属性，并获取历史及当前游戏的概览。
* `GameGrain` 表示单个游戏会话及相应的游戏逻辑。`GameGrain` 允许客户端落子并查看当前棋盘状态。
* `PairingGrain` 保存当前可供其他玩家加入的游戏列表。

调用流程如下：

![展示应用中调用关系的示意图](dataflow.png)

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

打开终端窗口，在命令提示符下执行以下命令：

```bash
dotnet run
```

游戏服务端将启动，你可以在浏览器中访问 `https://localhost:5000/` 与游戏交互。

如果希望观察它们如何形成集群，也可以启动更多宿主实例。此时应在命令行中添加 `InstanceId` 选项来区分各实例。生产应用会使用不同于本应用的“本地主机集群”方式（参见 _Program.cs_ 中通过 `UseLocalhostClustering` 配置集群的位置），因此无需该 `InstanceId` 选项。

```bash
dotnet run -- --InstanceId 1
```

由于游戏使用 Cookie 标识玩家，若要与自己对弈并体验游戏，需要使用独立的浏览器会话。
