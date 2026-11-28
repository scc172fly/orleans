<a id="microsoft-orleans-clustering-provider-for-azure-storage"></a>

# Microsoft Orleans 的 Azure Storage 集群提供程序

<a id="introduction"></a>

## 简介
Microsoft Orleans 的 Azure Storage 集群提供程序允许 Orleans Silo 使用 Azure Table Storage 组成集群。此提供程序使 Silo 能够相互发现、维护集群成员资格，以及检测并处理故障。

<a id="getting-started"></a>

## 入门
通过 NuGet 安装此包：

```shell
dotnet add package Microsoft.Orleans.Clustering.AzureStorage
```

<a id="example---configuring-azure-storage-clustering"></a>

## 示例：配置 Azure Storage 集群

```csharp
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;
using Orleans.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

// Define a grain interface
public interface IHelloGrain : IGrainWithStringKey
{
    Task<string> SayHello(string greeting);
}

// Implement the grain interface
public class HelloGrain : Grain, IHelloGrain
{
    public Task<string> SayHello(string greeting)
    {
        return Task.FromResult($"Hello, {greeting}!");
    }
}

var builder = Host.CreateApplicationBuilder(args)
    .UseOrleans(siloBuilder =>
    {
        siloBuilder
            // Configure Azure Table Storage for clustering
            .UseAzureStorageClustering(options =>
            {
                options.ConnectionString = "YOUR_AZURE_STORAGE_CONNECTION_STRING";
                options.TableName = "OrleansClustering"; // Optional: defaults to "OrleansClustering"
            });
    });

var host = builder.Build();
await host.StartAsync();

// Get a reference to a grain and call it
var client = host.Services.GetRequiredService<IClusterClient>();
var grain = client.GetGrain<IHelloGrain>("user123");
var response = await grain.SayHello("World");

// Print the result
Console.WriteLine($"Grain response: {response}");

// Keep the host running until the application is shut down
await host.WaitForShutdownAsync();
```

<a id="example---configuring-client-to-connect-to-cluster"></a>

## 示例：配置客户端以连接集群

```csharp
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
namespace ExampleGrains;

// Define a grain interface
public interface IHelloGrain : IGrainWithStringKey
{
    Task<string> SayHello(string greeting);
}

var clientBuilder = Host.CreateApplicationBuilder(args)
    .UseOrleansClient(clientBuilder =>
    {
        clientBuilder
            // Configure the client to use Azure Storage for clustering
            .UseAzureStorageClustering(options =>
            {
                options.ConnectionString = "YOUR_AZURE_STORAGE_CONNECTION_STRING";
                options.TableName = "OrleansClustering"; // Optional: defaults to "OrleansClustering"
            });
    });

var host = clientBuilder.Build();
await host.StartAsync();
var client = host.Services.GetRequiredService<IClusterClient>();

// Get a reference to a grain and call it
var grain = client.GetGrain<IHelloGrain>("user123");
var response = await grain.SayHello("World");

// Print the result
Console.WriteLine($"Grain response: {response}");

// Keep the host running until the application is shut down
await host.WaitForShutdownAsync();
```

<a id="documentation"></a>

## 文档
有关更详细的文档，请参阅：
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [集群提供程序](https://dotnet.github.io/orleans/docs/implementation/cluster-management/)
- [典型配置](https://dotnet.github.io/orleans/docs/host/configuration-guide/typical-configurations/)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如果遇到问题或希望提供反馈，请[在 GitHub 上提交 Issue](https://github.com/dotnet/orleans/issues)。
- 欢迎加入我们的 [Discord](https://aka.ms/orleans-discord) 社区。
- 关注 Twitter 账号 [@msftorleans](https://twitter.com/msftorleans)，获取 Orleans 公告。
- 欢迎参与贡献！请先阅读[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)。
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)。