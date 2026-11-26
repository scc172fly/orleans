<a id="microsoft-orleans-reminders-for-dynamodb"></a>

# Microsoft Orleans 催复 DynamoDB

<a id="introduction"></a>

## 简介
Microsoft Orleans 催复 DynamoDB 提供持久性 Orleans 使用亚马逊的提醒 DynamoDB。这样可以让您 Orleans 用于排入连续提醒的应用程序,即使 silo 重新启动或 grain 停止使用。

<a id="getting-started"></a>

## 入门
要使用此软件包, 请通过 NuGet安装 :

```shell
dotnet add package Microsoft.Orleans.Reminders.DynamoDB
```

<a id="example---configuring-dynamodb-reminders"></a>

## 示例 - 配置 DynamoDB 提醒
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

`UseDynamoDBReminderService` 配置提醒存储,独立于集束成员提供方。 AWS SDK 证书和剖析图解链在提醒选项略去明确密钥时提供证明文件。此示例使用点机容量和基础设施管理表格。

<a id="example---using-reminders-in-a-grain"></a>

## 示例 - 在 Grain中使用提醒
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

## 文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件
关于更全面的文件,请参见:
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [配置亚马逊 DynamoDB 提醒](https://dotnet.github.io/orleans/docs/grains/reminders/dynamodb/)
- [Grain 计时器和提醒通知](https://dotnet.github.io/orleans/docs/grains/timers-and-reminders/)
- [.NET 文档的 AWSSDK](https://docs.aws.amazon.com/sdk-for-net/index.html)

<a id="feedback--contributing"></a>

## 反馈和贡献
- 如果有任何问题或希望提供反馈,请 [GitHub上的开关](https://github.com/dotnet/orleans/issues)
- 加入我们社区 [不一致](https://aka.ms/orleans-discord)
- 跟随 [@msftorleans 地图上写着:](https://twitter.com/msftorleans) 推特账户发布 Orleans 通知
- 请审查我们的 [会费捐款准则](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 该项目在 [麻省理工学院许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)下获得许可证