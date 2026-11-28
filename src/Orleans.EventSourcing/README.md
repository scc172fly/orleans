<a id="microsoft-orleans-event-sourcing"></a>

# Microsoft Orleans 事件溯源

<a id="introduction"></a>

## 简介
Microsoft Orleans 事件溯源提供实现事件溯源 Grain 的支持。事件溯源是一种将状态变更记录为事件序列而非仅存储当前状态的模式。它提供完整的变更历史，并支持事件重放、时态查询及更稳健的审计等功能。

<a id="getting-started"></a>

## 入门
要使用此包，请通过 NuGet 安装：

```shell
dotnet add package Microsoft.Orleans.EventSourcing
```

<a id="example---creating-an-event-sourced-grain"></a>

## 示例 - 创建事件溯源 Grain

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

## 示例 - 为事件溯源配置存储

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

## 文档
有关更全面的文档，请参阅：
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [事件溯源概述](https://dotnet.github.io/orleans/docs/grains/event-sourcing/)
- [JournaledGrain 基础知识](https://dotnet.github.io/orleans/docs/grains/event-sourcing/journaledgrain-basics/)
- [复制实例](https://dotnet.github.io/orleans/docs/grains/event-sourcing/replicated-instances/)
- [日志一致性提供程序](https://dotnet.github.io/orleans/docs/grains/event-sourcing/log-consistency-providers/)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如果遇到问题或希望提供反馈，请[在 GitHub 上创建 Issue](https://github.com/dotnet/orleans/issues)
- 加入我们的 [Discord](https://aka.ms/orleans-discord) 社区
- 关注 Twitter 上的 [@msftorleans](https://twitter.com/msftorleans) 账号，获取 Orleans 公告
- 欢迎贡献！请阅读我们的[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)