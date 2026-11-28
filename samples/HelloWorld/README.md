---
languages:
- csharp
products:
- dotnet
- dotnet-orleans
page_type: sample
name: "Orleans Hello World 示例应用"
urlFragment: "orleans-hello-world-sample-app"
description: "演示简单 Hello World 应用的 Orleans 示例。"
---

<a id="orleans-hello-world-sample-application"></a>

# Orleans Hello World 示例应用

*Hello, World!* 应用是程序员的入门仪式，这是 Orleans 的 *Hello, World!* 示例。
示例由一个项目组成：启动基于 Orleans 的应用，向 Grain 发送消息，输出响应，并在用户按键后退出。

<a id="demonstrates"></a>

## 演示内容

* 如何开始使用 Orleans
* 如何定义和实现 Grain 接口
* 如何获取 Grain 引用并调用 Grain

要开始了解此应用，请打开 [`IHelloGrain.cs`](./IHelloGrain.cs)，其中包含以下接口声明：

```csharp
public interface IHelloGrain : IGrainWithStringKey
{
    Task<string> SayHello(string greeting);
}
```

这里定义了 `IHelloGrain` Grain 接口。它实现了 `IGrainWithStringKey`，因此可知它是一个 Grain 接口。这意味着，要获取实现 `IHelloGrain` 的 Grain 引用，需要使用字符串值标识 Grain 实例。在本例中，稍后在 [*Program.cs*](./Program.cs) 中可看到，我们使用字符串 `"friend"` 标识要与之通信的 Grain，但也可以使用任何其他字符串：

```csharp
var friend = grainFactory.GetGrain<IHelloGrain>("friend");
```

现在打开 [*HelloGrain.cs*](./HelloGrain.cs)，其中包含 `IHelloGrain` 接口的实现：

```csharp
public class HelloGrain : Grain, IHelloGrain
{
    public Task<string> SayHello(string greeting) =>
        Task.FromResult($"Hello, {greeting}!");
}
```

`HelloGrain` 继承了 `Grain` 基类型，因此可知它是一个 Grain 实现。
该类型用于标识应用中的 Grain 类。

`HelloGrain` 通过返回一个简单字符串 `$"Hello, {greeting}"` 来实现 `IHelloGrain`。

打开 [*Program.cs*](./Program.cs)，查看 Orleans 的配置方式：

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

此程序创建新的 [`HostBuilder`](https://docs.microsoft.com/dotnet/core/extensions/generic-host)，并调用 `UseOrleans` 扩展方法将 Orleans 添加到其中。
在该调用内，程序配置 Orleans 使用本地主机集群，以用于开发和测试场景。
然后程序启动宿主，并从其服务提供程序中获取 `IGrainFactory` 实例。
使用 `IGrainFactory` 可以获取 Grain 的*引用*。
本例需要名为 `"friend"` 的 `HelloGrain` 实例引用，因此调用 `grainFactory.GetGrain<IHelloGrain>("friend")`。
获取引用后即可使用它，调用 `friend.SayHello("Good morning!")` 并将结果输出到控制台。

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

```dotnetcli
dotnet run
```

应能在控制台中看到 `Hello, Good morning!!`。

我们首次调用 `HelloGrain` 的 `"friend"` 实例时（`friend.SayHello(...)`），Orleans 自动为我们创建了该实例。
开发者无需管理这些 Grain 的生命周期：Orleans 会按需激活它们，并在它们空闲时停用。
