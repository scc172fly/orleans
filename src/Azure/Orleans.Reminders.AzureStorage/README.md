<a id="microsoft-orleans-reminders-for-azure-storage"></a>

# Microsoft Orleans 的 Azure Storage 提醒器提供程序

<a id="introduction"></a>

## 简介
Microsoft Orleans 的 Azure Storage 提醒器提供程序使用 Azure Table Storage 持久化 Orleans 提醒器。这使 Orleans 应用程序可以调度持久化提醒器，即使 Silo 重启或 Grain 停用，提醒器也仍可触发。

<a id="getting-started"></a>

## 入门
通过 NuGet 安装此包：

```shell
dotnet add package Microsoft.Orleans.Reminders.AzureStorage
```

<a id="example---configuring-azure-storage-reminders"></a>

## 示例：配置 Azure Storage 提醒器
```csharp
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;
using Orleans.Hosting;

var builder = Host.CreateApplicationBuilder(args)
    .UseOrleans(siloBuilder =>
    {
        siloBuilder
            .UseLocalhostClustering()
            // Configure Azure Table Storage as reminder storage
            .UseAzureTableReminderService(options =>
            {
                options.ConnectionString = "YOUR_AZURE_STORAGE_CONNECTION_STRING";
                options.TableName = "OrleansReminders";
            });
    });

// Run the host
await builder.RunAsync();
```

<a id="example---using-reminders-in-a-grain"></a>

## 示例：在 Grain 中使用提醒器
```csharp
public interface IReminderGrain
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
有关更详细的文档，请参阅：
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [提醒器与定时器](https://dotnet.github.io/orleans/docs/grains/timers-and-reminders/)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如果遇到问题或希望提供反馈，请[在 GitHub 上提交 Issue](https://github.com/dotnet/orleans/issues)。
- 欢迎加入我们的 [Discord](https://aka.ms/orleans-discord) 社区。
- 关注 Twitter 账号 [@msftorleans](https://twitter.com/msftorleans)，获取 Orleans 公告。
- 欢迎参与贡献！请先阅读[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)。
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)。