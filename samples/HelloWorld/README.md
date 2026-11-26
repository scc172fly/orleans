---
languages:
- csharp
products:
- dotnet
- dotnet-orleans
page_type: sample
name: "Orleans Hello World sample app"
urlFragment: "orleans-hello-world-sample-app"
description: "展示简单哈罗世界应用程序的 Orleans 样本。"
---

<a id="orleans-hello-world-sample-application"></a>

# Orleans 你好世界样本应用

*你好 世界!* 申请是程序员的通过仪式,这是我们 *你好 世界!* 样本 Orleans.
样本由单一工程组成,该项目启动 Orleans- 基于应用程序、向 grain发送信息、打印回复和当用户按键时终止。

<a id="demonstrates"></a>

## 演示

* 如何启动 Orleans
* 如何定义和实施 grain 接口
* 如何获取 grain 的引用并调用 grain

要开始我们浏览应用程序, 请打开 [`IHelloGrain.cs`](./IHelloGrain.cs) , 您将会找到以下接口声明 :

```csharp
public interface IHelloGrain : IGrainWithStringKey
{
    Task<string> SayHello(string greeting);
}
```

定义 `IHelloGrain` grain 接口。 我们知道这是一个 grain 接口, 因为它执行 `IGrainWithStringKey`。 这意味着当我们想要用字符串值来查询一个 grain 执行的 `IHelloGrain`时, 我们将会使用字符串值来识别 grain 实例。 在我们的案例中, 正如您在 [*Program.cs*](./Program.cs)中稍后看到的那样, 我们将使用字符串 `"friend"` 来识别我们希望与它进行联系的 grain , 但这可能是任何字符串:

```csharp
var friend = grainFactory.GetGrain<IHelloGrain>("friend");
```

现在打开 [*HelloGrain.cs*](./HelloGrain.cs) ,我们将看到我们的 `IHelloGrain` 接口的实施:

```csharp
public class HelloGrain : Grain, IHelloGrain
{
    public Task<string> SayHello(string greeting) =>
        Task.FromResult($"Hello, {greeting}!");
}
```

我们知道, `HelloGrain` 是一种 grain 实施,因为它继承了 `Grain` 基准类型。
该类型用于识别应用程序中的 grain 类。

`HelloGrain` 返回一个简单的字符串来执行 `IHelloGrain` : `$"Hello, {greeting}"`。

打开 [*Program.cs*](./Program.cs) 以查看 Orleans 配置方式 :

```csharp
using var host = new HostBuilder()
    .UseOrleans(builder =>
    {
        builder.UseLocalhostClustering();
    })
    .Build();

await host.StartAsync();

// Get the grain factory
var grainFactory = host.Services.GetRequiredService<IGrainFactory>();

// Get a reference to the HelloGrain grain with the key "friend".
var friend = grainFactory.GetGrain<IHelloGrain>("friend");

// Call the grain and print the result to the console
var result = await friend.SayHello("Good morning!");
Console.WriteLine("\n\n{0}\n\n", result);

Console.WriteLine("Orleans is running.\nPress Enter to terminate...");
Console.ReadLine();
Console.WriteLine("Orleans is stopping...");

await host.StopAsync();
```

此程序创建一个新的 [`HostBuilder`](https://docs.microsoft.com/dotnet/core/extensions/generic-host) 并加 Orleans 呼唤它, `UseOrleans` 扩展方法。
在该呼叫中,它配置 Orleans 来使用本地主机群集,用于开发和测试设想方案。
程序然后启动主机并从服务供应商那里检索 `IGrainFactory` 实例。
使用 `IGrainFactory`,我们可以得到 *参考* 到 grain。
在这种情况下,我们希望提及 `HelloGrain` 命名实例 `"friend"`,我们因此呼吁 `grainFactory.GetGrain<IHelloGrain>("friend")`.
一旦我们找到参考, 我们可以把它用到 并拨打 `friend.SayHello("Good morning!")`, 将结果打印到控制台。

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

```dotnetcli
dotnet run
```

您应该看到 `Hello, Good morning!!` 打印在控制台上。

Orleans 断裂 `"friend"` 实例 `HelloGrain` 当我们初次喊叫的时候,我们立刻就召唤它。`friend.SayHello(...)`).
作为开发者,我们不需要管理这些 grains: Orleans 视需要激活它们的寿命,当它们闲置时,它会停止使用。
