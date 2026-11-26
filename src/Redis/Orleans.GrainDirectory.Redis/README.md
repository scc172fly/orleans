<a id="microsoft-orleans-grain-directory-for-redis"></a>

# Microsoft Orleans Grain 目录目录 Redis

<a id="introduction"></a>

## 简介
Microsoft Orleans Grain 目录目录 Redis 提供a grain 使用目录执行 Redis. The grain 用于定位活动目录 grain 横跨组群的 数例,而此套包允许 Orleans 将信息存储在 Redis.

<a id="getting-started"></a>

## 入门
要使用此软件包, 请通过 NuGet安装 :

```shell
dotnet add package Microsoft.Orleans.GrainDirectory.Redis
```

<a id="example---configuring-redis-grain-directory"></a>

## 示例 - 配置 Redis Grain 目录
```csharp
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;

var builder = Host.CreateApplicationBuilder(args)
    .UseOrleans(siloBuilder =>
    {
        siloBuilder
            .UseLocalhostClustering()
            // Configure Redis as the grain directory
            .UseRedisGrainDirectoryAsDefault(options =>
            {
                options.ConnectionString = "localhost:6379";
                options.Database = 0;
            });
    });

// Run the host
await builder.RunAsync();
```

<a id="configuration-via-microsoftextensionsconfiguration"></a>

## 通过 Microsoft.Extensions.Configuration配置配置

您可以配置 Orleans Redis grain 使用 `Microsoft.Extensions.Configuration` (例如 `appsettings.json`而不是将其配置为代码。当使用此方法时, Orleans 将自动读取配置 `Orleans` 节内

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
    "GrainDirectory": {
      "Default": {
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
var response = await grain.SayHello("Aspire Redis Grain Directory");

Console.WriteLine($"Grain response: {response}");
await host.WaitForShutdownAsync();
```

本示例假设您的 AppHost 项目已经像这样配置了 Redis :
```csharp
// In your AppHost/Program.cs
var builder = DistributedApplication.CreateBuilder(args);

var redis = builder.AddRedis("redis");

var orleans = builder.AddOrleans("orleans")
    .WithGrainDirectory("Default", redis);

builder.AddProject<Projects.MyOrleansApp>("orleans-app")
    .WithReference(orleans);

builder.Build().Run();
```

<a id="documentation"></a>

## 文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件
关于更全面的文件,请参见:
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [配置指南](https://dotnet.github.io/orleans/docs/host/configuration-guide/)
- [实施细节](https://dotnet.github.io/orleans/docs/implementation/)
- [Redis 文档](https://redis.io/documentation)

<a id="feedback--contributing"></a>

## 反馈和贡献
- 如果有任何问题或希望提供反馈,请 [GitHub上的开关](https://github.com/dotnet/orleans/issues)
- 加入我们社区 [不一致](https://aka.ms/orleans-discord)
- 跟随 [@msftorleans 地图上写着:](https://twitter.com/msftorleans) 推特账户发布 Orleans 通知
- 请审查我们的 [会费捐款准则](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 该项目在 [麻省理工学院许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)下获得许可证