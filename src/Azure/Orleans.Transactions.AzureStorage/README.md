<a id="microsoft-orleans-transactions-for-azure-storage"></a>

# Microsoft Orleans 交易的往来 Azure 储存

<a id="introduction"></a>

## 简介
Microsoft Orleans 交易的往来 Azure 储存库提供存储基础设施 Orleans 以 Azure 存储。 此软件包允许 Orleans 使用应用程序,使用ACID 交易的多个 grain 调用时 Azure 储存作为支持交易日志的仓库。

<a id="getting-started"></a>

## 入门
要使用此软件包, 请通过 NuGet安装 :

```shell
dotnet add package Microsoft.Orleans.Transactions.AzureStorage
```

<a id="example---configuring-azure-storage-for-transactions"></a>

## 示例 - 交易存储配置 Azure
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

## 示例 - 在 Grains中使用交易
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

## 文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件
关于更全面的文件,请参见:
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [交易交易](https://dotnet.github.io/orleans/docs/grains/transactions/)

<a id="feedback--contributing"></a>

## 反馈和贡献
- 如果有任何问题或希望提供反馈,请 [GitHub上的开关](https://github.com/dotnet/orleans/issues)
- 加入我们社区 [不一致](https://aka.ms/orleans-discord)
- 跟随 [@msftorleans 地图上写着:](https://twitter.com/msftorleans) 推特账户发布 Orleans 通知
- 请审查我们的 [会费捐款准则](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 该项目在 [麻省理工学院许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)下获得许可证