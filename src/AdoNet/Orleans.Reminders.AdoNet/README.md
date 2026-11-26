<a id="microsoft-orleans-reminders-for-adonet"></a>

# Microsoft Orleans 催复 ADO.NET

<a id="introduction"></a>

## 简介
Microsoft Orleans 催复 ADO.NET 提供持久性 Orleans 使用 ADO.NET- 兼容数据库(C)SQL 服务器 MySQL, PostgreSQL等))。这样可以允许您 Orleans 用于排入连续提醒的应用程序,即使 silo 重新启动或 grain 停止使用。

<a id="getting-started"></a>

## 入门
要使用此软件包, 请通过 NuGet安装 :

```shell
dotnet add package Microsoft.Orleans.Reminders.AdoNet
```

您还需要为您的数据库安装合适的 ADO.NET 提供者:

```shell
# For SQL Server
dotnet add package Microsoft.Data.SqlClient

# For MySQL
dotnet add package MySql.Data

# For PostgreSQL
dotnet add package Npgsql
```

<a id="example---configuring-adonet-reminders"></a>

## 示例 - 配置 ADO.NET 提醒
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

## 示例 - 在 Grain中使用提醒
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

## 文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件
关于更全面的文件,请参见:
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [提醒和计时器](https://dotnet.github.io/orleans/docs/grains/timers-and-reminders/)
- [ADO.NET 数据库设置](https://dotnet.github.io/orleans/docs/host/configuration-guide/adonet-configuration/)

<a id="feedback--contributing"></a>

## 反馈和贡献
- 如果有任何问题或希望提供反馈,请 [GitHub上的开关](https://github.com/dotnet/orleans/issues)
- 加入我们社区 [不一致](https://aka.ms/orleans-discord)
- 跟随 [@msftorleans 地图上写着:](https://twitter.com/msftorleans) 推特账户发布 Orleans 通知
- 请审查我们的 [会费捐款准则](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 该项目在 [麻省理工学院许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)下获得许可证
