---
languages:
- csharp
products:
- dotnet
- dotnet-orleans
page_type: sample
name: "Orleans Chirper 社交媒体示例应用"
urlFragment: "orleans-chirper-social-media-sample-app"
description: "实现社交媒体平台的 Orleans 示例应用。"
---

<a id="orleans-chirper-social-media-sample-app"></a>

# Orleans Chirper 社交媒体示例应用

![聊天客户端截图](screenshot.png)

本示例演示简单的社交网络发布/订阅系统，用户之间发送简短的文本消息。
发布者向关注自己的其他用户发送简短的 *"Chirp"* 消息（出于各种法律原因，请勿与 *"Tweets"* 混淆）。

示例由两个应用组成：`Chirper.Server` 托管 Orleans 和所有 Grain，`Chirper.Client` 托管 Orleans 客户端以及上图所示的终端界面。

示例演示 Orleans 的以下功能：

* Grain 持久化——将 Grain 状态存储在数据库中
* 可重入 Grain——允许多个调用并发交错执行
* Grain 观察者——向客户端推送消息

本项目使用内存持久化（参见 *Program.cs*），但可在不更改 Grain 代码的情况下替换为自选持久化提供程序。使用不同的持久化提供程序只需修改配置。

<a id="why-orleans"></a>

## 为何使用 Orleans

Orleans 使 Chirper 用户网络能够使用非常简单的 C# 代码描述，同时易于横向扩展，以应对不断增长的用户数量和 Chirp 消息量。

<a id="how-is-it-modeled"></a>

## 如何建模

Chirper 用户被建模为 Grain。

通过将每个用户建模为一个 Grain，消息传递负载得以分布，每个 Grain 负责将该用户生成的消息转发给关注该用户的其他用户。

这些 Grain 实现三种不同的 Grain 接口，以表示实体的三个功能侧面——`IChirperPublisher`、`IChirperSubscriber` 和 `IChirperAccount`。

此外还有 `IChirperViewer` 观察者接口，应用可在不成为关注者的情况下订阅特定 Chirper 用户的状态变化。此观察者接口通常用于编写 `ChirperClient` 等客户端 UI 应用。

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
