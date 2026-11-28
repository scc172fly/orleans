<a id="microsoft-orleans-server"></a>

# Microsoft Orleans Server

<a id="introduction"></a>

## 简介
Microsoft Orleans Server 是包含运行 Orleans Silo（服务器）所需全部组件的元包。通过引用单个包而非分别引用多个包，可以简化 Orleans 服务器的设置。

<a id="getting-started"></a>

## 入门
要使用此包，请通过 NuGet 安装：

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

## 文档
有关更全面的文档，请参阅：
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Orleans 服务器（Silo）配置](https://dotnet.github.io/orleans/docs/host/configuration-guide/server-configuration/)
- [.NET 通用主机](https://learn.microsoft.com/dotnet/core/extensions/generic-host)
- [Grain 持久化](https://dotnet.github.io/orleans/docs/grains/grain-persistence/)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如果遇到问题或希望提供反馈，请[在 GitHub 上创建 Issue](https://github.com/dotnet/orleans/issues)
- 加入我们的 [Discord](https://aka.ms/orleans-discord) 社区
- 关注 Twitter 上的 [@msftorleans](https://twitter.com/msftorleans) 账号，获取 Orleans 公告
- 欢迎贡献！请阅读我们的[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)