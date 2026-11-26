<a id="microsoft-orleans-event-sourcing"></a>

# Microsoft Orleans 事件

<a id="introduction"></a>

## 简介
Microsoft Orleans 事件搜索为实施事件源码 grains提供支持。事件来源是一种模式,即状态变化记录为一系列事件,而不仅仅是储存当前状态。它提供了完整的变化历史,并允许产生强大的能力,如重播事件、时间查询和更强有力的审计。

<a id="getting-started"></a>

## 入门
要使用此软件包, 请通过 NuGet安装 :

```shell
dotnet add package Microsoft.Orleans.EventSourcing
```

<a id="example---creating-an-event-sourced-grain"></a>

## 示例 - 创建事件源 Grain

```csharp
using Orleans;
using Orleans.EventSourcing;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

// Define grain state and events
namespace BankAccount;

public class BankAccountState
{
    public decimal Balance { get; set; }
    public string AccountHolder { get; set; }
    public int Version { get; set; }
}

public class DepositEvent
{
    public decimal Amount { get; set; }
}

public class WithdrawalEvent
{
    public decimal Amount { get; set; }
}

// Grain interface
public interface IBankAccountGrain : IGrainWithStringKey
{
    Task<decimal> GetBalance();
    Task Deposit(decimal amount);
    Task Withdraw(decimal amount);
    Task<IReadOnlyList<object>> GetHistory();
}

// Event-sourced grain implementation using JournaledGrain
public class BankAccountGrain : JournaledGrain<BankAccountState, object>, IBankAccountGrain
{
    public async Task<decimal> GetBalance()
    {
        // The state is automatically hydrated from the event log
        return State.Balance;
    }

    public async Task Deposit(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentException("Deposit amount must be positive");

        // Record the event - this will be persisted and applied to state
        RaiseEvent(new DepositEvent { Amount = amount });
        
        // Confirm the event is persisted
        await ConfirmEvents();
    }

    public async Task Withdraw(decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentException("Withdrawal amount must be positive");
            
        if (State.Balance < amount)
            throw new InvalidOperationException("Insufficient funds");

        // Record the event
        RaiseEvent(new WithdrawalEvent { Amount = amount });
        
        // Confirm the event is persisted
        await ConfirmEvents();
    }

    public Task<IReadOnlyList<object>> GetHistory()
    {
        // Return the complete history of events
        return Task.FromResult<IReadOnlyList<object>>(RetrieveConfirmedEvents(0, Version).ToList());
    }

    // Event handlers to update the state based on events
    protected override void ApplyEvent(object @event)
    {
        switch (@event)
        {
            case DepositEvent deposit:
                State.Balance += deposit.Amount;
                break;
                
            case WithdrawalEvent withdrawal:
                State.Balance -= withdrawal.Amount;
                break;
        }
    }
}
```

<a id="example---configuring-event-sourcing-with-storage"></a>

## 实例 - 与存储一起观测的配置事件

```csharp
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.EventSourcing;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;

var builder = Host.CreateApplicationBuilder(args)
    .UseOrleans(siloBuilder =>
    {
        siloBuilder
            .UseLocalhostClustering()
            // Configure the log consistency provider for event sourcing
            .AddLogStorageBasedLogConsistencyProvider("LogStorage")
            // Configure a storage provider to store the events
            .AddMemoryGrainStorage("PubSubStore")
            .ConfigureServices(services =>
            {
                // Configure default log consistency provider
                services.Configure<JournaledGrainOptions>(options =>
                {
                    options.DefaultLogConsistencyProvider = "LogStorage";
                });
            });
    });

var host = builder.Build();
await host.StartAsync();

// Get a reference to a grain and call it
var client = host.Services.GetRequiredService<IClusterClient>();
var bankAccount = client.GetGrain<IBankAccountGrain>("account-123");

// Call grain methods
await bankAccount.Deposit(100);
await bankAccount.Withdraw(25);
var balance = await bankAccount.GetBalance();

// Print the result
Console.WriteLine($"Account balance: ${balance}");

var history = await bankAccount.GetHistory();
Console.WriteLine($"Transaction history: {history.Count} events");

// Keep the host running until the application is shut down
await host.WaitForShutdownAsync();
```

<a id="documentation"></a>

## 文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件
关于更全面的文件,请参见:
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [事件抛光概览](https://dotnet.github.io/orleans/docs/grains/event-sourcing/)
- [JournaledGrain 基本知识](https://dotnet.github.io/orleans/docs/grains/event-sourcing/journaledgrain-basics/)
- [重复的事例](https://dotnet.github.io/orleans/docs/grains/event-sourcing/replicated-instances/)
- [日对一致性提供者](https://dotnet.github.io/orleans/docs/grains/event-sourcing/log-consistency-providers/)

<a id="feedback--contributing"></a>

## 反馈和贡献
- 如果有任何问题或希望提供反馈,请 [GitHub上的开关](https://github.com/dotnet/orleans/issues)
- 加入我们社区 [不一致](https://aka.ms/orleans-discord)
- 跟随 [@msftorleans 地图上写着:](https://twitter.com/msftorleans) 推特账户发布 Orleans 通知
- 请审查我们的 [会费捐款准则](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 该项目在 [麻省理工学院许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)下获得许可证