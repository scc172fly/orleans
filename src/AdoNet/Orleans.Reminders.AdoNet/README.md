<a id="microsoft-orleans-reminders-for-adonet"></a>

# Microsoft Orleans ADO.NET 提醒器提供程序

<a id="introduction"></a>

## 简介
Microsoft Orleans ADO.NET 提醒器提供程序使用与 ADO.NET 兼容的数据库（SQL Server、MySQL、PostgreSQL 等）持久化 Orleans 提醒器，使 Orleans 应用程序能够安排在 Silo 重启或 Grain 停用后仍可触发的持久化提醒器。

<a id="getting-started"></a>

## 入门
要使用此包，请通过 NuGet 安装：

```shell
dotnet add package Microsoft.Orleans.Reminders.AdoNet
```

还需要为所用数据库安装相应的 ADO.NET 提供程序：

```shell
# For SQL Server
dotnet add package Microsoft.Data.SqlClient

# For MySQL
dotnet add package MySql.Data

# For PostgreSQL
dotnet add package Npgsql
```

<a id="example---configuring-adonet-reminders"></a>

## 示例 - 配置 ADO.NET 提醒器
```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Hosting;
using Example;

// Create a host builder
var builder = Host.CreateApplicationBuilder(args);
builder.UseOrleans(siloBuilder =>
{
    siloBuilder
        .UseLocalhostClustering()
        // Configure ADO.NET as reminder storage
        .UseAdoNetReminderService(options =>
        {
            options.Invariant = "Microsoft.Data.SqlClient";  // For SQL Server
            options.ConnectionString = "Server=localhost;Database=OrleansReminders;User ID=orleans;******;";
        });
});

// Build and start the host
var host = builder.Build();
await host.StartAsync();

// Get a grain reference and use it
var grain = host.Services.GetRequiredService<IGrainFactory>().GetGrain<IReminderGrain>("my-reminder-grain");
await grain.StartReminder("DailyReport");
Console.WriteLine("Reminder started successfully!");

// Keep the host running until the application is shut down
await host.WaitForShutdownAsync();
```

<a id="example---using-reminders-in-a-grain"></a>

## 示例 - 在 Grain 中使用提醒器
```csharp
using System;
using System.Threading.Tasks;
using Orleans;
using Orleans.Runtime;

namespace Example;

public interface IReminderGrain : IGrainWithStringKey
{
    Task StartReminder(string reminderName);
    Task StopReminder();
}

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

<a id="documentation"></a>

## 文档
有关更全面的文档，请参阅：
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [提醒器与定时器](https://dotnet.github.io/orleans/docs/grains/timers-and-reminders/)
- [ADO.NET 数据库设置](https://dotnet.github.io/orleans/docs/host/configuration-guide/adonet-configuration/)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如果遇到问题或希望提供反馈，请[在 GitHub 上创建 Issue](https://github.com/dotnet/orleans/issues)
- 加入我们的 [Discord](https://aka.ms/orleans-discord) 社区
- 关注 Twitter 上的 [@msftorleans](https://twitter.com/msftorleans) 账号，获取 Orleans 公告
- 欢迎贡献！请阅读我们的[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)
