<a id="microsoft-orleans-server"></a>

# Microsoft Orleans 服务器

<a id="introduction"></a>

## 简介
Microsoft Orleans 服务器是一个包含运行 Orleans silo (服务器)所有必要组成部分的元包。它通过提供单一的软件包参考,而不是要求您单独引用多个软件包来简化设置 Orleans 服务器的过程。

<a id="getting-started"></a>

## 入门
要使用此软件包, 请通过 NuGet安装 :

```shell
dotnet add package Microsoft.Orleans.Server
```

<a id="example---creating-an-orleans-silo-host"></a>

## 示例 - 创建 Orleans Silo 主机

```csharp
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;
using Orleans.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

// Define a grain interface
namespace MyGrainNamespace;

public interface IMyGrain : IGrainWithStringKey
{
    Task<string> DoSomething();
}

// Implement the grain interface
public class MyGrain : Grain, IMyGrain
{
    public Task<string> DoSomething()
    {
        return Task.FromResult("Done something!");
    }
}


// Create the host
var builder = Host.CreateApplicationBuilder(args)
    .UseOrleans(siloBuilder =>
    {
        siloBuilder
            .UseLocalhostClustering();
    });

// Start the host
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
- [Orleans 服务器 (silo)配置](https://dotnet.github.io/orleans/docs/host/configuration-guide/server-configuration/)
- [.NET 通用主机](https://learn.microsoft.com/dotnet/core/extensions/generic-host)
- [Grain 持久性](https://dotnet.github.io/orleans/docs/grains/grain-persistence/)

<a id="feedback--contributing"></a>

## 反馈和贡献
- 如果有任何问题或希望提供反馈,请 [GitHub上的开关](https://github.com/dotnet/orleans/issues)
- 加入我们社区 [不一致](https://aka.ms/orleans-discord)
- 跟随 [@msftorleans 地图上写着:](https://twitter.com/msftorleans) 推特账户发布 Orleans 通知
- 请审查我们的 [会费捐款准则](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 该项目在 [麻省理工学院许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)下获得许可证