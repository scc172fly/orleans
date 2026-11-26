---
languages:
- csharp
products:
- dotnet
- dotnet-orleans
page_type: sample
name: "Orleans Text Adventure Game"
urlFragment: "orleans-text-adventure-game"
description: "以 C# 写成的文本冒险游戏的例子, 使用 Orleans。"
---

<a id="orleans-text-adventure-game"></a>

# Orleans 文字冒险游戏

在有图形用户界面之前,在游戏控制台和大型多玩游戏时代之前, 有一些 VT100 终端, 还有 [冒险 巨岩洞](https://en.wikipedia.org/wiki/Colossal_Cave_Adventure)、 [佐克](https://en.wikipedia.org/wiki/Zork)和 [Microsoft 冒险](https://en.wikipedia.org/wiki/Microsoft_Adventure)。按照今天的标准,它可能很逊, 当时是一个由怪物、鸣叫鸟和你能接收到的东西组成的神奇世界。 这是这个样本的灵感。

<a id="demonstrates"></a>

## 演示

* 如何使用 grains构建应用程序(在此情况下为游戏)
* 如何连接外部客户端到 Orleans 集集 (`ClientBuilder`)

系统由两部分组成:一个服务器可执行文件 *AdventureServer* 和客户端可执行文件 *AdventureClient*服务器读取游戏数据文件, `AdventureMap.json` 默认, 并初始化 `RoomGrain` 与游戏数据使用该游戏数据。客户端连接到服务器,并使用 `IPlayerGrain` 在服务器上, `IPlayerGrain` 执行的 `PlayerGrain`,因此任何呼吁 `IPlayerGrain` 选择的路线,相应的 `PlayerGrain` 对话框。客户通过调用对游戏发布命令 `IPlayerGrain.Play(command)`,其中 `command` 是玩家在命令提示时输入的字符串。 `PlayerGrain` 向 `RoomGrain` 与房间互动。

这是一个简单的游戏,只有几句动词可以让游戏理解:

* `look` - 检查现有会议室
* `go <direction>` - 搬到另一个房间
* `north`、 `south`、 `east`、 `west` - `go north`等的快捷键
* `kill <target>` - 杀死目标
* `drop <thing>` - 从播放器的列表中丢弃一些东西
* `take <thing>` - 在播放器的列表中添加当前房间的项目
* `inv` 或 `inventory` - 审查播放器的清
* `end` - 退出游戏

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

要运行游戏,请在命令提示(打开到样本的基准目录)执行以下操作,以运行服务器:

``` bash
dotnet run --project AdventureServer
```

您应该看到服务器启动并打印行 `Press any key to exit`。

在单独的终端中执行以下操作以启动客户端并玩游戏:

``` bash
dotnet run --project AdventureClient
```
