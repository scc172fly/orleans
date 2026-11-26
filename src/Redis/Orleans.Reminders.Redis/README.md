<a id="microsoft-orleans-reminders-for-redis"></a>

# Microsoft Orleans 催复 Redis

<a id="introduction"></a>

## 简介
Microsoft Orleans 催复 Redis 提供持久性 Orleans 使用 Redis。这样可以让您 Orleans 用于排入连续提醒的应用程序,即使 silo 重新启动或 grain 停止使用。

<a id="getting-started"></a>

## 入门
要使用此软件包, 请通过 NuGet安装 :

```shell
dotnet add package Microsoft.Orleans.Reminders.Redis
```

<a id="example---configuring-redis-reminders"></a>

## 示例 - 配置 Redis 提醒
```csharp
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;
using Orleans.Hosting;

var builder = Host.CreateApplicationBuilder(args)
    .UseOrleans(siloBuilder =>
    {
        siloBuilder
            .UseLocalhostClustering()
            // Configure Redis as reminder storage
            .UseRedisReminderService(options =>
            {
                options.ConnectionString = "localhost:6379";
                options.Database = 0;
                options.KeyPrefix = "reminder-"; // Optional prefix for Redis keys
            });
    });

// Run the host
await builder.RunAsync();
```

<a id="example---using-reminders-in-a-grain"></a>

## 示例 - 在 Grain中使用提醒
```csharp
public class ReminderGrain : Grain, IReminderGrain, IRemindable
{
    private string _reminderName = "MyReminder";

    public async Task StartReminder(string reminderName)
    {
        _reminderName = reminderName;
        
        // Register a persistent reminder
        await RegisterOrUpdateReminder(
            reminderName,
            TimeSpan.FromMinutes(2),  // Time to delay before the first tick (must be > 1 minute)
            TimeSpan.FromMinutes(5)); // Period of the reminder (must be > 1 minute)
    }

    public async Task StopReminder()
    {
        // Find and unregister the reminder
        var reminder = await GetReminder(_reminderName);
        if (reminder != null)
        {
            await UnregisterReminder(reminder);
        }
    }

    public Task ReceiveReminder(string reminderName, TickStatus status)
    {
        // This method is called when the reminder ticks
        Console.WriteLine($"Reminder {reminderName} triggered at {DateTime.UtcNow}. Status: {status}");
        return Task.CompletedTask;
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
var grain = client.GetGrain<IReminderGrain>("user123");
await grain.StartReminder("AspireReminder");

Console.WriteLine("Reminder started with Aspire Redis!");
await host.WaitForShutdownAsync();
```

本示例假设您的 AppHost 项目已经像这样配置了 Redis :
```csharp
// In your AppHost/Program.cs
var builder = DistributedApplication.CreateBuilder(args);

var redis = builder.AddRedis("redis");

var orleans = builder.AddOrleans("orleans")
    .WithReminders(redis);

builder.AddProject<Projects.MyOrleansApp>("orleans-app")
    .WithReference(orleans);

builder.Build().Run();
```

<a id="documentation"></a>

## 文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件
关于更全面的文件,请参见:
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [提醒和计时器](https://dotnet.github.io/orleans/docs/grains/timers-and-reminders/)
- [Redis 文档](https://redis.io/documentation)

<a id="feedback--contributing"></a>

## 反馈和贡献
- 如果有任何问题或希望提供反馈,请 [GitHub上的开关](https://github.com/dotnet/orleans/issues)
- 加入我们社区 [不一致](https://aka.ms/orleans-discord)
- 跟随 [@msftorleans 地图上写着:](https://twitter.com/msftorleans) 推特账户发布 Orleans 通知
- 请审查我们的 [会费捐款准则](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 该项目在 [麻省理工学院许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)下获得许可证