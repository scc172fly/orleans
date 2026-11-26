<a id="microsoft-orleans-core-library"></a>

# Microsoft Orleans 核心库

<a id="introduction"></a>

## 简介
Microsoft Orleans 核心是客户和服务器应用程序使用的主要图书馆,为 Orleans 应用程序提供必要的运行时间组件,包括序列、通信和核心主机基础设施。

<a id="getting-started"></a>

## 入门
要使用此软件包, 请通过 NuGet安装 :

```shell
dotnet add package Microsoft.Orleans.Core
```

当您引用 Orleans SDK 或 Orleans 客户端/服务器元包时,此软件包将被自动包含。

<a id="example---configuring-a-client"></a>

## 实例 - 客户配置

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Configuration;
using System;
using System.Threading.Tasks;

// Define a grain interface
namespace MyGrainNamespace;

public interface IHelloGrain : IGrainWithStringKey
{
    Task<string> SayHello(string greeting);
}

// Implement the grain interface
public class HelloGrain : Grain, IHelloGrain
{
    public Task<string> SayHello(string greeting)
    {
        return Task.FromResult($"Hello! I got: {greeting}");
    }
}

// Create a client
var builder = Host.CreateApplicationBuilder(args)
    .UseOrleansClient(client =>
    {
        client.UseLocalhostClustering();
    });

var host = builder.Build();
await host.StartAsync();

// Get a reference to a grain and call it
var grain = host.Services.GetRequiredService<IClusterClient>().GetGrain<IHelloGrain>("grain-id");
var response = await grain.SayHello("Hello from client!");

// Print the result
Console.WriteLine($"Response: {response}");

// Keep the host running until the application is shut down
await host.WaitForShutdownAsync();
```

<a id="documentation"></a>

## 文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件
关于更全面的文件,请参见:
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [客户配置](https://dotnet.github.io/orleans/docs/host/client/)
- [.NET中依赖性注射](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection)

<a id="feedback--contributing"></a>

## 反馈和贡献
- 如果有任何问题或希望提供反馈,请 [GitHub上的开关](https://github.com/dotnet/orleans/issues)
- 加入我们社区 [不一致](https://aka.ms/orleans-discord)
- 跟随 [@msftorleans 地图上写着:](https://twitter.com/msftorleans) 推特账户发布 Orleans 通知
- 请审查我们的 [会费捐款准则](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 该项目在 [麻省理工学院许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)下获得许可证