<a id="microsoft-orleans-core-library"></a>

# Microsoft Orleans 核心库

<a id="introduction"></a>

## 简介
Microsoft Orleans Core 是客户端和服务器应用程序共同使用的主要库。它提供 Orleans 应用程序所需的运行时组件，包括序列化、通信及核心托管基础设施。

<a id="getting-started"></a>

## 入门
要使用此包，请通过 NuGet 安装：

```shell
dotnet add package Microsoft.Orleans.Core
```

引用 Orleans SDK 或 Orleans 客户端/服务器元包时，会自动包含此包。

<a id="example---configuring-a-client"></a>

## 示例 - 配置客户端

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

## 文档
有关更全面的文档，请参阅：
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [客户端配置](https://dotnet.github.io/orleans/docs/host/client/)
- [.NET 中的依赖注入](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如果遇到问题或希望提供反馈，请[在 GitHub 上创建 Issue](https://github.com/dotnet/orleans/issues)
- 加入我们的 [Discord](https://aka.ms/orleans-discord) 社区
- 关注 Twitter 上的 [@msftorleans](https://twitter.com/msftorleans) 账号，获取 Orleans 公告
- 欢迎贡献！请阅读我们的[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)