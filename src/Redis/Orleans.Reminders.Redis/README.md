<a id="microsoft-orleans-reminders-for-redis"></a>

# Microsoft Orleans Redis 提醒器提供程序

<a id="introduction"></a>

## 简介
Microsoft Orleans Redis 提醒器提供程序使用 Redis 持久化 Orleans 提醒器，使 Orleans 应用程序能够安排即使在 Silo 重启或 Grain 停用后也会触发的持久化提醒器。

<a id="getting-started"></a>

## 入门
要使用此包，请通过 NuGet 安装：

```shell
dotnet add package Microsoft.Orleans.Reminders.Redis
```

<a id="example---configuring-redis-reminders"></a>

## 示例 - 配置 Redis 提醒器
```csharp
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;
using Orleans.Hosting;

var builder = Host.CreateApplicationBuilder(args)
    .UseOrleans(siloBuilder =>
    {
        siloBuilder
            .UseLocalhostClustering()
            // 使用 Redis 存储提醒器
            .UseRedisReminderService(options =>
            {
                options.ConnectionString = "localhost:6379";
                options.Database = 0;
                options.KeyPrefix = "reminder-"; // Redis 键的可选前缀
            });
    });

// 运行宿主
await builder.RunAsync();
```

<a id="example---using-reminders-in-a-grain"></a>

## 示例 - 在 Grain 中使用提醒器
```csharp
public class ReminderGrain : Grain, IReminderGrain, IRemindable
{
    private string _reminderName = "MyReminder";

    public async Task StartReminder(string reminderName)
    {
        _reminderName = reminderName;
        
        // 注册持久化提醒器
        await RegisterOrUpdateReminder(
            reminderName,
            TimeSpan.FromMinutes(2),  // 首次触发前的等待时间，可设为零
            TimeSpan.FromMinutes(5)); // 重复周期须为正，且不小于配置的最小周期（默认为一分钟）
    }

    public async Task StopReminder()
    {
        // 查找并注销提醒器
        var reminder = await GetReminder(_reminderName);
        if (reminder != null)
        {
            await UnregisterReminder(reminder);
        }
    }

    public Task ReceiveReminder(string reminderName, TickStatus status)
    {
        // 每次提醒器触发时都会调用此方法
        Console.WriteLine($"Reminder {reminderName} triggered at {DateTime.UtcNow}. Status: {status}");
        return Task.CompletedTask;
    }
}
```

<a id="configuration-via-microsoftextensionsconfiguration"></a>

## 通过 Microsoft.Extensions.Configuration 配置

可以使用 `Microsoft.Extensions.Configuration`（例如 `appsettings.json`）配置 Orleans Redis 提醒器，而不必在代码中配置。采用这种方式时，Orleans 会自动从 `Orleans` 节读取配置。

> **注意**：可以使用 `"ProviderType": "Redis"` 或 `"ProviderType": "AzureRedisCache"`，二者均受支持且功能等效。

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
    "Reminders": {
      "ProviderType": "Redis",
      "ServiceKey": "redis",
      "Database": 0,
      "KeyPrefix": "reminder-"
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

// 添加 Aspire 的默认服务配置
builder.AddServiceDefaults();

// 通过 Aspire 客户端集成添加 Redis
builder.AddKeyedRedisClient("redis");

// 添加 Orleans
builder.UseOrleans();

var host = builder.Build();
await host.StartAsync();

// 获取 Grain 引用并调用其方法
var client = host.Services.GetRequiredService<IClusterClient>();
var grain = client.GetGrain<IReminderGrain>("user123");
await grain.StartReminder("AspireReminder");

Console.WriteLine("Reminder started with Aspire Redis!");
await host.WaitForShutdownAsync();
```

本示例假定 AppHost 项目已按以下方式配置 Redis：
```csharp
// 在 AppHost/Program.cs 中配置
var builder = DistributedApplication.CreateBuilder(args);

var redis = builder.AddRedis("redis");

var orleans = builder.AddOrleans("orleans")
    .WithReminders(redis);

builder.AddProject<Projects.MyOrleansApp>("orleans-app")
    .WithReference(orleans);

builder.Build().Run();
```

<a id="documentation"></a>

## 文档
有关更全面的文档，请参阅：
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [提醒器与定时器](https://dotnet.github.io/orleans/docs/grains/timers-and-reminders/)
- [Redis 文档](https://redis.io/documentation)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如果遇到问题或希望提供反馈，请[在 GitHub 上创建 Issue](https://github.com/dotnet/orleans/issues)
- 加入我们的 [Discord](https://aka.ms/orleans-discord) 社区
- 关注 Twitter 上的 [@msftorleans](https://twitter.com/msftorleans) 账号，获取 Orleans 公告
- 欢迎贡献！请阅读我们的[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)