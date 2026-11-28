<a id="microsoft-orleans-reminders-for-dynamodb"></a>

# Microsoft Orleans DynamoDB 提醒器提供程序

<a id="introduction"></a>

## 简介
Microsoft Orleans DynamoDB 提醒器提供程序利用 Amazon DynamoDB 持久化 Orleans 提醒器，使 Orleans 应用程序能够安排持久化提醒器，即使 Silo 重启或 Grain 停用后也能触发。

<a id="getting-started"></a>

## 入门
要使用此包，请通过 NuGet 安装：

```shell
dotnet add package Microsoft.Orleans.Reminders.DynamoDB
```

<a id="example---configuring-dynamodb-reminders"></a>

## 示例 - 配置 DynamoDB 提醒器
```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Configuration;
using Orleans.Hosting;

var builder = Host.CreateApplicationBuilder(args)
    .UseOrleans(siloBuilder =>
    {
        siloBuilder
            .UseLocalhostClustering()
            // Configure DynamoDB as reminder storage
            .UseDynamoDBReminderService(options =>
            {
                options.Service = "us-east-1";
                options.TableName = "OrleansReminders";
                options.UseProvisionedThroughput = false;
                options.CreateIfNotExists = false;
                options.UpdateIfExists = false;
            });
    });

// Run the host
var host = builder.Build();
await host.StartAsync();

// Get a reference to the grain
var reminderGrain = host.Services.GetRequiredService<IGrainFactory>()
    .GetGrain<IReminderGrain>("my-reminder-grain");

// Start the reminder
await reminderGrain.StartReminder("ExampleReminder");
Console.WriteLine("Reminder started!");

// Keep the host running until the application is shut down
await host.WaitForShutdownAsync();
```

`UseDynamoDBReminderService` 独立于集群成员资格提供程序配置提醒器存储。提醒器选项未指定显式密钥时，由 AWS SDK 凭据和配置文件解析链提供凭据。本示例使用按需容量模式及由基础设施管理的表。

<a id="example---using-reminders-in-a-grain"></a>

## 示例 - 在 Grain 中使用提醒器
```csharp
using System;
using System.Threading.Tasks;
using Orleans;
using Orleans.Runtime;

namespace ReminderExample;

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
- [配置 Amazon DynamoDB 提醒器](https://dotnet.github.io/orleans/docs/grains/reminders/dynamodb/)
- [Grain 定时器与提醒器](https://dotnet.github.io/orleans/docs/grains/timers-and-reminders/)
- [AWS SDK for .NET 文档](https://docs.aws.amazon.com/sdk-for-net/index.html)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如果遇到问题或希望提供反馈，请[在 GitHub 上创建 Issue](https://github.com/dotnet/orleans/issues)
- 加入我们的 [Discord](https://aka.ms/orleans-discord) 社区
- 关注 Twitter 上的 [@msftorleans](https://twitter.com/msftorleans) 账号，获取 Orleans 公告
- 欢迎贡献！请阅读我们的[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)