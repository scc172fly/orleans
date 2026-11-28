<a id="microsoft-orleans-clustering-for-redis"></a>

# Microsoft Orleans Redis 集群提供程序

<a id="introduction"></a>

## 简介
Microsoft Orleans Redis 集群提供程序使用 Redis 为 Microsoft Orleans 提供集群成员资格功能，使 Orleans Silo 能够以 Redis 为后备存储进行协调并组成集群。

<a id="getting-started"></a>

## 入门
要使用此包，请通过 NuGet 安装：

```shell
dotnet add package Microsoft.Orleans.Clustering.Redis
```

<a id="example---configuring-redis-membership"></a>

## 示例 - 配置 Redis 成员资格
```csharp
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

var builder = Host.CreateApplicationBuilder(args)
    .UseOrleans(siloBuilder =>
    {
        siloBuilder
            // Configure Redis as the membership provider
            .UseRedisClustering(options =>
            {
                options.ConnectionString = "localhost:6379";
                options.Database = 0;
            });
    });

var host = builder.Build();
await host.StartAsync();

// Get a reference to a grain and call it
var client = host.Services.GetRequiredService<IClusterClient>();
var grain = client.GetGrain<IHelloGrain>("user123");
var response = await grain.SayHello("Redis");

// Print the result
Console.WriteLine($"Grain response: {response}");

// Keep the host running until the application is shut down
await host.WaitForShutdownAsync();
```

<a id="example---client-configuration"></a>

## 示例 - 客户端配置
```csharp
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;
using Orleans;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

var clientBuilder = Host.CreateApplicationBuilder(args)
    .UseOrleansClient(builder =>
    {
        builder
            // Configure Redis as the gateway provider
            .UseRedisGatewayListProvider(options =>
            {
                options.ConnectionString = "localhost:6379";
                options.Database = 0;
            });
    });

var host = clientBuilder.Build();
await host.StartAsync();
var client = host.Services.GetRequiredService<IClusterClient>();

// Get a reference to a grain and call it
var grain = client.GetGrain<IHelloGrain>("user123");
var response = await grain.SayHello("Redis Client");

// Print the result
Console.WriteLine($"Grain response: {response}");

// Keep the host running until the application is shut down
await host.WaitForShutdownAsync();
```

<a id="configuration-via-microsoftextensionsconfiguration"></a>

## 通过 Microsoft.Extensions.Configuration 配置

可以使用 `Microsoft.Extensions.Configuration`（例如 `appsettings.json`）配置 Orleans Redis 集群，而不必在代码中配置。采用这种方式时，Orleans 会自动从 `Orleans` 节读取配置。

> **注意**：可以使用 `"ProviderType": "Redis"` 或 `"ProviderType": "AzureRedisCache"`，二者均受支持且功能等效。

<a id="example---appsettingsjson-silo"></a>

### 示例 - appsettings.json (Silo)
```json
{
  "ConnectionStrings": {
    "redis": "localhost:6379"
  },
  "Orleans": {
    "ClusterId": "my-cluster",
    "ServiceId": "MyOrleansService",
    "Clustering": {
      "ProviderType": "Redis",
      "ServiceKey": "redis"
    }
  }
}
```

<a id="example---appsettingsjson-client"></a>

### 示例 - appsettings.json (Client)
```json
{
  "ConnectionStrings": {
    "redis": "localhost:6379"
  },
  "Orleans": {
    "ClusterId": "my-cluster",
    "ServiceId": "MyOrleansService", 
    "Clustering": {
      "ProviderType": "Redis",
      "ServiceKey": "redis"
    }
  }
}
```

<a id="net-aspire-integration"></a>

### .NET Aspire 集成

对于使用 .NET Aspire 的应用程序，可以考虑使用 [.NET Aspire Redis 集成](https://learn.microsoft.com/dotnet/aspire/caching/stackexchange-redis-integration)，它提供简化的 Redis 配置、自动服务发现、健康检查及遥测功能。Aspire 集成会自动配置连接字符串，Orleans 可以通过配置系统使用这些连接字符串。

<a id="example---programcs-with-aspire-redis-integration"></a>

#### 示例 - 集成 Aspire Redis 的 Program.cs
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
var grain = client.GetGrain<IHelloGrain>("user123");
var response = await grain.SayHello("Aspire Redis");

Console.WriteLine($"Grain response: {response}");
await host.WaitForShutdownAsync();
```

本示例假定 AppHost 项目已按以下方式配置 Redis：
```csharp
// In your AppHost/Program.cs
var builder = DistributedApplication.CreateBuilder(args);

var redis = builder.AddRedis("redis");

var orleans = builder.AddOrleans("orleans")
    .WithClustering(redis);

builder.AddProject<Projects.MyOrleansApp>("orleans-app")
    .WithReference(orleans);

builder.Build().Run();
```

<a id="documentation"></a>

## 文档
有关更全面的文档，请参阅：
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [配置指南](https://dotnet.github.io/orleans/docs/host/configuration-guide/)
- [Orleans 集群](https://dotnet.github.io/orleans/docs/implementation/cluster-management/)
- [Redis 文档](https://redis.io/documentation)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如果遇到问题或希望提供反馈，请[在 GitHub 上创建 Issue](https://github.com/dotnet/orleans/issues)
- 加入我们的 [Discord](https://aka.ms/orleans-discord) 社区
- 关注 Twitter 上的 [@msftorleans](https://twitter.com/msftorleans) 账号，获取 Orleans 公告
- 欢迎贡献！请阅读我们的[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)