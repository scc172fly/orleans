---
languages:
- csharp
products:
- dotnet
- dotnet-orleans
page_type: sample
name: "Orleans 文字冒险游戏"
urlFragment: "orleans-text-adventure-game"
description: "使用 Orleans、以 C# 编写的文字冒险游戏示例。"
---

<a id="orleans-text-adventure-game"></a>

# Orleans 文字冒险游戏

在图形用户界面、游戏主机和大型多人游戏出现之前，已有 VT100 终端，以及 [Colossal Cave Adventure](https://en.wikipedia.org/wiki/Colossal_Cave_Adventure)、[Zork](https://en.wikipedia.org/wiki/Zork) 和 [Microsoft Adventure](https://en.wikipedia.org/wiki/Microsoft_Adventure) 等游戏。按今天的标准，它们可能显得简陋，但在当时却是一个充满怪物、鸟鸣和可拾取物品的奇妙世界。本示例便受其启发。

<a id="demonstrates"></a>

## 演示内容

* 如何使用 Grain 组织应用（本例为游戏）
* 如何将外部客户端连接到 Orleans 集群（`ClientBuilder`）

系统由两部分组成：名为 *AdventureServer* 的服务端可执行程序，以及名为 *AdventureClient* 的客户端可执行程序。服务端读取游戏数据文件（默认为 `AdventureMap.json`），并使用这些数据初始化 `RoomGrain` 实例。客户端连接到服务端，通过 `IPlayerGrain` 接口与游戏交互。在服务端，`IPlayerGrain` 由 `PlayerGrain` 实现，因此对 `IPlayerGrain` 的调用都会路由到相应的 `PlayerGrain` 实例。客户端调用 `IPlayerGrain.Play(command)` 向游戏发送命令，其中 `command` 是玩家在命令提示符中输入的字符串。`PlayerGrain` 解释并执行各条命令，必要时调用 `RoomGrain` 与房间交互。

这是一款简单的游戏，只支持少数操作命令：

* `look` - 查看当前房间
* `go <direction>` - 移动到其他房间。
* `north`、`south`、`east`、`west` - `go north` 等命令的简写
* `kill <target>` - 击杀目标
* `drop <thing>` - 从玩家背包中丢弃物品
* `take <thing>` - 将当前房间中的物品加入玩家背包
* `inv` 或 `inventory` - 查看玩家背包
* `end` - 退出游戏

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

要运行游戏，请在命令提示符中（当前目录为示例根目录）执行以下命令启动服务端：

``` bash
dotnet run --project AdventureServer
```

应能看到服务端启动，最终输出 `Press any key to exit`。

在另一个终端中执行以下命令，启动客户端并开始游戏：

``` bash
dotnet run --project AdventureClient
```
