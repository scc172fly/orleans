---
languages:
- csharp
products:
- dotnet
- dotnet-orleans
page_type: sample
name: "Orleans Gaming Presence service sample"
urlFragment: "orleans-gaming-presence-service-sample"
description: "展示赌博服务使用情况的 Orleans 抽样服务。"
---

<a id="orleans-gaming-presence-service-sample"></a>

# Orleans 赌博服务抽样

本样本展示了游戏存在服务,其中游戏服务器(由 *LoadGenerator* 应用)发送周期性心心心心心向云服务发送(由 *PresenceService* 内含一个相应的 `PresenceGrain` 负责解开心跳信息,并反映游戏状态 `PlayerGrain` 和 `GameGrain` grains这一点的影响可从客户申请中看出(*PlayerWatcher*),该选项可以测量当前游戏会话的玩家。每次返回新的游戏会话时,客户端将创建一个新 `LoggerGameObserver` 订阅游戏 grain 以便使用 `IGameObserver` 接口。

![以上文字的直观表达](PresenceService.svg)

<a id="sample-prerequisites"></a>

## 抽样的前提条件

本样本以C##和目标 .NET 10 来写,要求有 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 或以后的。

<a id="building-the-sample"></a>

## 建立样本

下载和运行样本时,请遵循以下步骤:

1. 下载并解开样本的平面
2. 视觉演播室(2022年或以后):
    1. 在菜单栏上,选择 **文件** > **打开** > **项目/解决方案**。
    2. 打开解压后的示例目录，再打开其中的 C# 项目文件（`.csproj`）。
    3. 按 <kbd>F5</kbd> 调试运行，或按 <kbd>Ctrl</kbd>+<kbd>F5</kbd> 直接运行。
3. 从命令行 :
   1. 导航到持有未设偏移样本代码的文件夹 。
   2. 在命令行,类型 [`dotnet run`](https://docs.microsoft.com/dotnet/core/tools/dotnet-run)。

要在命令行上逐步建立和运行样本,请使用以下命令:

使用 .NET CLI构建解决方案:

```dotnetcli
dotnet build
```

启动服务器进程 :

```dotnetcli
dotnet run --project ./src/PresenceService
```

在单独的终端窗口中,启动播放器监视器:

```dotnetcli
dotnet run --project ./src/PlayerWatcher
```

在单独的终端窗口中,发射载荷发电机:

```dotnetcli
dotnet run --project ./src/LoadGenerator
```
