<a id="microsoft-orleans-streaming-for-azure-storage-queues"></a>

# Microsoft Orleans 的 Azure Storage 队列流提供程序

<a id="introduction"></a>

## 简介
Microsoft Orleans 的 Azure Storage 流提供程序提供了基于 Azure Storage 队列的 Orleans 流提供程序实现。这使应用程序可以使用 Azure Storage 队列作为底层消息基础设施来发布和订阅事件流。

<a id="getting-started"></a>

## 入门
通过 NuGet 安装此包：

```shell
dotnet add package Microsoft.Orleans.Streaming.AzureStorage
```

<a id="example---configuring-azure-storage-queues-streaming"></a>

## 示例：配置 Azure Storage 队列流
```csharp
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;
using Orleans.Streams;

var builder = Host.CreateApplicationBuilder(args)
    .UseOrleans(siloBuilder =>
    {
        siloBuilder
            .UseLocalhostClustering()
            // Configure Azure Storage Queues as a stream provider
            .AddAzureQueueStreams(
                name: "AzureQueueStreamProvider", 
                b => b.ConfigureAzureQueue(ob => ob.Configure((options, dep) =>
                {
                    options.ConfigureTestDefaults();
                    options.QueueNames = Enumerable.Range(0, 8).Select(num => $"{dep.Value.ClusterId}-{num}").ToList();
                })));
    });

// Run the host
await builder.RunAsync();
```

<a id="example---using-azure-storage-queue-streams-in-a-grain"></a>

## 示例：在 Grain 中使用 Azure Storage 队列流
```csharp
// Producer grain
public class ProducerGrain : Grain, IProducerGrain
{
    private IAsyncStream<string> _stream;

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        // Get a reference to a stream
        var streamProvider = GetStreamProvider("AzureQueueStreamProvider");
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
        var streamProvider = GetStreamProvider("AzureQueueStreamProvider");
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

<a id="documentation"></a>

## 文档
有关更详细的文档，请参阅：
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Orleans 流](https://dotnet.github.io/orleans/docs/streaming/)
- [流提供程序](https://dotnet.github.io/orleans/docs/streaming/stream-providers/)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如果遇到问题或希望提供反馈，请[在 GitHub 上提交 Issue](https://github.com/dotnet/orleans/issues)。
- 欢迎加入我们的 [Discord](https://aka.ms/orleans-discord) 社区。
- 关注 Twitter 账号 [@msftorleans](https://twitter.com/msftorleans)，获取 Orleans 公告。
- 欢迎参与贡献！请先阅读[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)。
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)。