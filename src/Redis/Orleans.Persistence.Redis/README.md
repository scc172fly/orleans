<a id="microsoft-orleans-persistence-for-redis"></a>

# Microsoft Orleans 持久性 Redis

<a id="introduction"></a>

## 简介
Microsoft Orleans 持久性 Redis 提供 grain 持久性或持久性 Microsoft Orleans 使用 Redis。这样可以让您 grains 坚持自己在 Redis 当他们被重新装填的时候,他们能用它来加以利用。 Redis用于快速存取的记忆数据存储

<a id="getting-started"></a>

## 入门
要使用此软件包, 请通过 NuGet安装 :

```shell
dotnet add package Microsoft.Orleans.Persistence.Redis
```

<a id="example---configuring-redis-persistence"></a>

## 示例 - 配置 Redis 持久性
```csharp
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;
using Orleans.Hosting;

var builder = Host.CreateApplicationBuilder(args)
    .UseOrleans(siloBuilder =>
    {
        siloBuilder
            .UseLocalhostClustering()
            // Configure Redis as grain storage
            .AddRedisGrainStorage(
                name: "redisStore",
                configureOptions: options =>
                {
                    options.ConnectionString = "localhost:6379";
                    options.Database = 0;
                    options.UseJson = true; // Serializes grain state as JSON
                    options.KeyPrefix = "grain-"; // Optional prefix for Redis keys
                });
    });

// Run the host
await builder.RunAsync();
```

<a id="example---using-grain-storage-in-a-grain"></a>

## 实例 - 使用 Grain 储存在 Grain
```csharp
// Define grain state class

public class MyGrainState
{
    public string Data { get; set; }
    public int Version { get; set; }
}

// Grain implementation that uses Redis storage
public class MyGrain : Grain, IMyGrain, IGrainWithStringKey
{
    private readonly IPersistentState<MyGrainState> _state;

    public MyGrain([PersistentState("state", "redisStore")] IPersistentState<MyGrainState> state)
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

<a id="configuration-via-microsoftextensionsconfiguration"></a>

## 通过 Microsoft.Extensions.Configuration配置配置

您可以配置 Orleans Redis 使用 `Microsoft.Extensions.Configuration` (例如 `appsettings.json`而不是将其配置为代码。当使用此方法时, Orleans 将自动读取配置 `Orleans` 节内

> **注**:您可以使用 `"ProviderType": "Redis"` 或 `"ProviderType": "AzureRedisCache"` ----两者都得到支持,功能等同。

<a id="example---appsettingsjson"></a>

### 示例 - appsettings.json
```json
{
  "ConnectionStrings": {
    "redis": "localhost:6379"
  },
  "Orleans": {
    "ClusterId": "my-cluster",
    "ServiceId": "MyOrleansService",
    "GrainStorage": {
      "redisStore": {
        "ProviderType": "Redis",
        "ServiceKey": "redis"
      }
    }
  }
}
```

<a id="net-aspire-integration"></a>

### .NET 委托整合

用于 .NET 参考,考虑使用 [.NET 委托 Redis 整合](https://learn.microsoft.com/dotnet/aspire/caching/stackexchange-redis-integration) 简化 Redis 自动服务配置、自动服务发现、健康检查和遥测。 Orleans 可以通过配置系统消耗。

<a id="example---programcs-with-aspire-redis-integration"></a>

#### 示例 - Program.cs 与委托 Redis 整合
```csharp
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;
using Microsoft.Extensions.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);

// Add service defaults (Aspire configurations)
builder.AddServiceDefaults();

// Add Redis via Aspire client integration
builder.AddKeyedRedisClient("redis");

// Add Orleans
builder.UseOrleans();

var host = builder.Build();
await host.StartAsync();

// Get a reference to a grain and call it
var client = host.Services.GetRequiredService<IClusterClient>();
var grain = client.GetGrain<IMyGrain>("user123");
await grain.SetData("Hello from Aspire Redis!");
var data = await grain.GetData();

Console.WriteLine($"Grain data: {data}");
await host.WaitForShutdownAsync();
```

本示例假设您的 AppHost 项目已经像这样配置了 Redis :
```csharp
// In your AppHost/Program.cs
var builder = DistributedApplication.CreateBuilder(args);

var redis = builder.AddRedis("redis");

var orleans = builder.AddOrleans("orleans")
    .WithGrainStorage("redisStore", redis);

builder.AddProject<Projects.MyOrleansApp>("orleans-app")
    .WithReference(orleans);

builder.Build().Run();
```

<a id="documentation"></a>

## 文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件
关于更全面的文件,请参见:
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Grain 持久性](https://dotnet.github.io/orleans/docs/grains/grain-persistence/)
- [Redis 文档](https://redis.io/documentation)

<a id="feedback--contributing"></a>

## 反馈和贡献
- 如果有任何问题或希望提供反馈,请 [GitHub上的开关](https://github.com/dotnet/orleans/issues)
- 加入我们社区 [不一致](https://aka.ms/orleans-discord)
- 跟随 [@msftorleans 地图上写着:](https://twitter.com/msftorleans) 推特账户发布 Orleans 通知
- 请审查我们的 [会费捐款准则](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 该项目在 [麻省理工学院许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)下获得许可证