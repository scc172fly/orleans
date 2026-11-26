---
languages:
- csharp
products:
- dotnet
- dotnet-orleans
page_type: sample
name: "Orleans Chirper Social Media sample app"
urlFragment: "orleans-chirper-social-media-sample-app"
description: "执行社交媒体平台的 Orleans 样本应用程序。"
---

<a id="orleans-chirper-social-media-sample-app"></a>

# Orleans 奇伯社交媒体样本应用

![聊天客户端的截图](screenshot.png)

该样本展示了一个简单的社交网络酒吧/子系统,用户之间发送短信。
出版商短发 *"点击"* (不与 *"双色"*向跟踪这些技术的任何其他用户提供。

样本由两个应用程序组成, `Chirper.Server` 是主机 Orleans 和所有 grains, `Chirper.Client` 是主机客户端 Orleans 客户端,以及上述截图所看到的终端界面。

样本显示了 Orleans的下列特征:

* Grain 持久性 - 将 grain 状态储存在数据库中
* Reentrant grains - 允许多次通话同时互插
* Grain 观察员 - 向客户发送推推信息的观察员

在此项目中,使用了模拟持久性(见 *Program.cs*),但可以替代您选择的持久性提供者,而不必更改 grain 代码。只有配置需要改变才能使用不同的持久性提供者。

<a id="why-orleans"></a>

## 为什么 Orleans

Orleans 允许通过非常简单的C#代码描述Chirper用户网络,同时便于其扩展,以处理数量不断增加的用户和声频信息的数量。

<a id="how-is-it-modeled"></a>

## 如何建模

奇珀用户以 grains模式建模。

通过将每个用户模拟为 grain,信息负荷分布在各地,每个 grain 处理该用户生成的信息传递给跟踪这些信息的任何其他用户。

grains 安装三个不同的 grain 接口,代表这些实体的三个职能方面—— `IChirperPublisher`、 `IChirperSubscriber` 和 `IChirperAccount`

还有一个 `IChirperViewer` 观察员界面,用于从某个特定的 Chirper 用户中订阅更改状态的应用程序,而不必成为追随者。这种观察员界面通常用于撰写客户用户 UI 应用程序,例如 `ChirperClient`。

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
