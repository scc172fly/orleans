<a id="microsoft-orleans-persistence-for-azure-storage"></a>

# Microsoft Orleans 的 Azure Storage 持久化提供程序

<a id="introduction"></a>

## 简介
Microsoft Orleans 的 Azure Storage 持久化提供程序使用 Azure Storage（Blob 和 Table）为 Orleans 提供 Grain 持久化功能。这使 Grain 能够将状态持久化到 Azure Storage 中，并在重新激活时重新加载状态。

<a id="getting-started"></a>

## 入门
通过 NuGet 安装此包：

```shell
dotnet add package Microsoft.Orleans.Persistence.AzureStorage
```

<a id="example---configuring-azure-storage-persistence"></a>

## 示例：配置 Azure Storage 持久化
```csharp
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;
using Orleans.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

// Define grain interface
public interface IMyGrain : IGrainWithStringKey
{
    Task SetData(string data);
    Task<string> GetData();
}


var builder = Host.CreateApplicationBuilder(args)
    .UseOrleans(siloBuilder =>
    {
        siloBuilder
            .UseLocalhostClustering()
            // Configure Azure Table Storage as grain storage
            .AddAzureTableGrainStorage(
                name: "tableStore",
                configureOptions: options =>
                {
                    options.ConnectionString = "YOUR_AZURE_STORAGE_CONNECTION_STRING";
                })
            // Configure Azure Blob Storage as grain storage
            .AddAzureBlobGrainStorage(
                name: "blobStore",
                configureOptions: options =>
                {
                    options.ConnectionString = "YOUR_AZURE_STORAGE_CONNECTION_STRING";
                });
    });

var host = builder.Build();
await host.StartAsync();

// Get a reference to a grain and call it
var client = host.Services.GetRequiredService<IClusterClient>();
var grain = client.GetGrain<IMyGrain>("user123");
await grain.SetData("Hello from Azure Storage!");
var response = await grain.GetData();

// Print the result
Console.WriteLine($"Grain data: {response}");

// Keep the host running until the application is shut down
await host.WaitForShutdownAsync();
```

<a id="example---using-grain-storage-in-a-grain"></a>

## 示例：在 Grain 中使用 Grain 存储
```csharp
using System;
using System.Threading.Tasks;
using Orleans;
using Orleans.Runtime;
namespace ExampleGrains;

// Define grain state class

public class MyGrainState
{
    public string Data { get; set; }
    public int Version { get; set; }
}

// Grain implementation that uses the Azure storage
public class MyGrain : Grain, IMyGrain, IGrainWithStringKey
{
    private readonly IPersistentState<MyGrainState> _state;

    public MyGrain([PersistentState("state", "tableStore")] IPersistentState<MyGrainState> state)
    {
        _state = state;
    }

    public async Task SetData(string data)
    {
        _state.State.Data = data;
        _state.State.Version++;
        await _state.WriteStateAsync();
    }

    public Task<string> GetData()
    {
        return Task.FromResult(_state.State.Data);
    }
}
```

<a id="documentation"></a>

## 文档
有关更详细的文档，请参阅：
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Grain 持久化](https://dotnet.github.io/orleans/docs/grains/grain-persistence/)
- [Azure Storage 持久化](https://dotnet.github.io/orleans/docs/grains/grain-persistence/azure-storage/)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如果遇到问题或希望提供反馈，请[在 GitHub 上提交 Issue](https://github.com/dotnet/orleans/issues)。
- 欢迎加入我们的 [Discord](https://aka.ms/orleans-discord) 社区。
- 关注 Twitter 账号 [@msftorleans](https://twitter.com/msftorleans)，获取 Orleans 公告。
- 欢迎参与贡献！请先阅读[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)。
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)。