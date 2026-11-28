<a id="microsoft-orleans-transactions-for-azure-storage"></a>

# Microsoft Orleans 的 Azure Storage 事务提供程序

<a id="introduction"></a>

## 简介
Microsoft Orleans 的 Azure Storage 事务提供程序提供了将 Orleans 事务日志存储于 Azure Storage 的基础设施。此包允许 Orleans 应用程序使用 Azure Storage 作为事务日志后端存储，实现跨多个 Grain 调用的 ACID 事务。

<a id="getting-started"></a>

## 入门
通过 NuGet 安装此包：

```shell
dotnet add package Microsoft.Orleans.Transactions.AzureStorage
```

<a id="example---configuring-azure-storage-for-transactions"></a>

## 示例：配置 Azure Storage 事务
```csharp
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Transactions;

var builder = Host.CreateApplicationBuilder(args)
    .UseOrleans(siloBuilder =>
    {
        siloBuilder
            .UseLocalhostClustering()
            // Enable transactions
            .AddAzureTableTransactionalStateStorage(
                name: "TransactionStore",
                configureOptions: options =>
                {
                    options.ConnectionString = "YOUR_AZURE_STORAGE_CONNECTION_STRING";
                })
            .UseTransactions();
    });

// Run the host
await builder.RunAsync();
```

<a id="example---using-transactions-in-grains"></a>

## 示例：在 Grain 中使用事务
```csharp
// A grain with transactional state
public class MyTransactionalGrain : Grain, IMyTransactionalGrain
{
    private readonly ITransactionalState<MyState> _state;

    // Inject the transactional state
    public MyTransactionalGrain(
        [TransactionalState("state", "TransactionStore")]
        ITransactionalState<MyState> state)
    {
        _state = state;
    }

    // Method that performs a transaction
    [Transaction(TransactionOption.Create)]
    public async Task Transfer(string otherGrainKey, int amount)
    {
        // Read our state within the transaction
        var myState = await _state.PerformRead(state => state);
        
        // Ensure we have enough balance
        if (myState.Balance < amount)
            throw new InvalidOperationException("Insufficient funds");
            
        // Update our state within the transaction
        await _state.PerformUpdate(s => s.Balance -= amount);
        
        // Call another grain within the same transaction
        var otherGrain = GrainFactory.GetGrain<IMyTransactionalGrain>(otherGrainKey);
        await otherGrain.Deposit(amount);
    }

    // Method that participates in a transaction
    [Transaction(TransactionOption.Join)]
    public Task Deposit(int amount)
    {
        // Update state within the joined transaction
        return _state.PerformUpdate(s => s.Balance += amount);
    }

    // Read operation within a transaction
    [Transaction(TransactionOption.CreateOrJoin)]
    public Task<int> GetBalance()
    {
        return _state.PerformRead(s => s.Balance);
    }
}

// State class

public class MyState
{
    public int Balance { get; set; }
}
```

<a id="documentation"></a>

## 文档
有关更详细的文档，请参阅：
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [事务](https://dotnet.github.io/orleans/docs/grains/transactions/)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如果遇到问题或希望提供反馈，请[在 GitHub 上提交 Issue](https://github.com/dotnet/orleans/issues)。
- 欢迎加入我们的 [Discord](https://aka.ms/orleans-discord) 社区。
- 关注 Twitter 账号 [@msftorleans](https://twitter.com/msftorleans)，获取 Orleans 公告。
- 欢迎参与贡献！请先阅读[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)。
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)。