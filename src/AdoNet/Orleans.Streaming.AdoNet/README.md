<a id="microsoft-orleans-streaming-for-adonet"></a>

# Microsoft Orleans ADO.NET 流提供程序

<a id="introduction"></a>

## 简介
Microsoft Orleans ADO.NET 流提供程序使用与 ADO.NET 兼容的数据库（SQL Server、MySQL、PostgreSQL 等）实现 Orleans 流提供程序，以关系数据库为底层基础设施，支持事件流的发布与订阅。

<a id="getting-started"></a>

## 入门
要使用此包，请通过 NuGet 安装：

```shell
dotnet add package Microsoft.Orleans.Streaming.AdoNet
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

<a id="example---configuring-adonet-streaming"></a>

## 示例 - 配置 ADO.NET 流
```csharp
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;
using Orleans.Streams;

var builder = Host.CreateApplicationBuilder(args)
    .UseOrleans(siloBuilder =>
    {
        siloBuilder
            .UseLocalhostClustering()
            // Configure ADO.NET as a stream provider
            .AddAdoNetStreams(
                name: "AdoNetStreamProvider",
                configureOptions: options =>
                {
                    options.Invariant = "Microsoft.Data.SqlClient";  // For SQL Server
                    options.ConnectionString = "Server=localhost;Database=OrleansStreaming;User ID=orleans;******;";
                });
    });

// Run the host
await builder.RunAsync();
```

<a id="example---using-adonet-streams-in-a-grain"></a>

## 示例 - 在 Grain 中使用 ADO.NET 流
```csharp
// Producer grain
public class ProducerGrain : Grain, IProducerGrain
{
    private IAsyncStream<string> _stream;

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        // Get a reference to a stream
        var streamProvider = GetStreamProvider("AdoNetStreamProvider");
        _stream = streamProvider.GetStream<string>(Guid.NewGuid(), "MyStreamNamespace");

        return base.OnActivateAsync(cancellationToken);
    }

    public async Task SendMessage(string message)
    {
        // Send a message to the stream
        await _stream.OnNextAsync(message);
    }
}

// Consumer grain
public class ConsumerGrain : Grain, IConsumerGrain, IAsyncObserver<string>
{
    private StreamSubscriptionHandle<string> _subscription;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        // Get a reference to a stream
        var streamProvider = GetStreamProvider("AdoNetStreamProvider");
        var stream = streamProvider.GetStream<string>(this.GetPrimaryKey(), "MyStreamNamespace");

        // Subscribe to the stream
        _subscription = await stream.SubscribeAsync(this);

        await base.OnActivateAsync(cancellationToken);
    }

    public Task OnNextAsync(string item, StreamSequenceToken token = null)
    {
        Console.WriteLine($"Received message: {item}");
        return Task.CompletedTask;
    }

    public Task OnCompletedAsync()
    {
        Console.WriteLine("Stream completed");
        return Task.CompletedTask;
    }

    public Task OnErrorAsync(Exception ex)
    {
        Console.WriteLine($"Stream error: {ex.Message}");
        return Task.CompletedTask;
    }
}
```

<a id="receiver-shutdown"></a>

## 接收器关闭

关闭或重新分配队列时，每个接收器停止接纳新操作，并等待已接纳的出队和确认操作完成，包括批次转换与回执记录。然后使用当前回执释放待处理消息，使另一接收器能够及时将其出队。

关闭超时分别限制排空等待以及随后释放查询的耗时。如果排空超时，接收器将记录警告；待处理消息将在数据库中的可见性超时到期后可以重新投递。

<a id="documentation"></a>

## 文档
有关更全面的文档，请参阅：
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Orleans 流](https://dotnet.github.io/orleans/docs/streaming/)
- [流提供程序](https://dotnet.github.io/orleans/docs/streaming/stream-providers/)
- [ADO.NET 数据库设置](https://dotnet.github.io/orleans/docs/host/configuration-guide/adonet-configuration/)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如果遇到问题或希望提供反馈，请[在 GitHub 上创建 Issue](https://github.com/dotnet/orleans/issues)
- 加入我们的 [Discord](https://aka.ms/orleans-discord) 社区
- 关注 Twitter 上的 [@msftorleans](https://twitter.com/msftorleans) 账号，获取 Orleans 公告
- 欢迎贡献！请阅读我们的[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)
