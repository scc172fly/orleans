<a id="microsoft-orleans-persistence-for-azure-storage"></a>

# Microsoft Orleans 持久性 Azure 储存

<a id="introduction"></a>

## 简介
Microsoft Orleans 持久性 Azure 储存备有 grain 持久性或持久性 Microsoft Orleans 使用 Azure 存储 (Blob 和表) 。允许您使用 grains 坚持自己在 Azure 装上再装满,当他们重新被取出的时候。

<a id="getting-started"></a>

## 入门
要使用此软件包, 请通过 NuGet安装 :

```shell
dotnet add package Microsoft.Orleans.Persistence.AzureStorage
```

<a id="example---configuring-azure-storage-persistence"></a>

## 示例 - 配置 Azure 存储持久性
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

## 实例 - 使用 Grain 储存在 Grain
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

## 文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件
关于更全面的文件,请参见:
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Grain 持久性](https://dotnet.github.io/orleans/docs/grains/grain-persistence/)
- [Azure 储存持久性](https://dotnet.github.io/orleans/docs/grains/grain-persistence/azure-storage/)

<a id="feedback--contributing"></a>

## 反馈和贡献
- 如果有任何问题或希望提供反馈,请 [GitHub上的开关](https://github.com/dotnet/orleans/issues)
- 加入我们社区 [不一致](https://aka.ms/orleans-discord)
- 跟随 [@msftorleans 地图上写着:](https://twitter.com/msftorleans) 推特账户发布 Orleans 通知
- 请审查我们的 [会费捐款准则](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 该项目在 [麻省理工学院许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)下获得许可证