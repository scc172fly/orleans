<a id="microsoft-orleans-client"></a>

# Microsoft Orleans 客户端

<a id="introduction"></a>

## 简介
Microsoft Orleans 客户端是一个元包,包含从客户应用程序连接到 Orleans 群集的一切必要组件。它提供了一个简化的方法,通过提供单一的软件包参考来设置 Orleans 客户端,而不是要求您单独引用多个软件包。

<a id="getting-started"></a>

## 入门
要使用此软件包, 请通过 NuGet安装 :

```shell
dotnet add package Microsoft.Orleans.Client
```

<a id="example---creating-an-orleans-client"></a>

## 示例 - 创建 Orleans 客户端

```csharp
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
namespace ExampleGrains;

// Define a grain interface
public interface IMyGrain : IGrainWithStringKey
{
    Task<string> DoSomething();
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
var client = host.Services.GetRequiredService<IClusterClient>();
var grain = client.GetGrain<IMyGrain>("my-grain-id");
var result = await grain.DoSomething();

// Print the result
Console.WriteLine($"Result: {result}");

// Keep the host running until the application is shut down
await host.WaitForShutdownAsync();
```

<a id="documentation"></a>

## 文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件
关于更全面的文件,请参见:
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Orleans 客户端配置](https://dotnet.github.io/orleans/docs/host/client/)
- [Grain 引用](https://dotnet.github.io/orleans/docs/grains/grain-references/)
- [Orleans 请求上下文](https://dotnet.github.io/orleans/docs/grains/request-context/)

<a id="feedback--contributing"></a>

## 反馈和贡献
- 如果有任何问题或希望提供反馈,请 [GitHub上的开关](https://github.com/dotnet/orleans/issues)
- 加入我们社区 [不一致](https://aka.ms/orleans-discord)
- 跟随 [@msftorleans 地图上写着:](https://twitter.com/msftorleans) 推特账户发布 Orleans 通知
- 请审查我们的 [会费捐款准则](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 该项目在 [麻省理工学院许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)下获得许可证