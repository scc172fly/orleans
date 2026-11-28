<a id="microsoft-orleans-persistence-for-azure-cosmos-db"></a>

# Microsoft Orleans 的 Azure Cosmos DB 持久化提供程序

<a id="introduction"></a>

## 简介
Microsoft Orleans 的 Azure Cosmos DB 持久化提供程序使用 Azure Cosmos DB 为 Orleans 提供 Grain 持久化功能。这使 Grain 能够将状态持久化到 Azure Cosmos DB 中，并在重新激活时重新加载状态，从而为 Orleans 应用程序提供全球分布式、多模型数据库服务。

<a id="getting-started"></a>

## 入门
通过 NuGet 安装此包：

```shell
dotnet add package Microsoft.Orleans.Persistence.Cosmos
```

<a id="example---configuring-azure-cosmos-db-persistence"></a>

## 示例：配置 Azure Cosmos DB 持久化
```csharp
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;
using Orleans.Hosting;

var builder = Host.CreateApplicationBuilder(args)
    .UseOrleans(siloBuilder =>
    {
        siloBuilder
            .UseLocalhostClustering()
            // Configure Azure Cosmos DB as grain storage
            .AddCosmosGrainStorage(
                name: "cosmosStore",
                configureOptions: options =>
                {
                    options.AccountEndpoint = "https://YOUR_COSMOS_ENDPOINT";
                    options.AccountKey = "YOUR_COSMOS_KEY";
                    options.DB = "YOUR_DATABASE_NAME";
                    options.CanCreateResources = true;
                });
    });

// Run the host
await builder.RunAsync();
```

<a id="example---using-grain-storage-in-a-grain"></a>

## 示例：在 Grain 中使用 Grain 存储
```csharp
// Define grain state class

public class MyGrainState
{
    public string Data { get; set; }
    public int Version { get; set; }
}

// Grain implementation that uses the Cosmos DB storage
public class MyGrain : Grain, IMyGrain, IGrainWithStringKey
{
    private readonly IPersistentState<MyGrainState> _state;

    public MyGrain([PersistentState("state", "cosmosStore")] IPersistentState<MyGrainState> state)
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
- [Azure Storage 提供程序](https://dotnet.github.io/orleans/docs/grains/grain-persistence/azure-storage/)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如果遇到问题或希望提供反馈，请[在 GitHub 上提交 Issue](https://github.com/dotnet/orleans/issues)。
- 欢迎加入我们的 [Discord](https://aka.ms/orleans-discord) 社区。
- 关注 Twitter 账号 [@msftorleans](https://twitter.com/msftorleans)，获取 Orleans 公告。
- 欢迎参与贡献！请先阅读[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)。
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)。