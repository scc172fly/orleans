<a id="microsoft-orleans-stream-provider-for-azure-event-hubs"></a>

# Microsoft Orleans 的 Azure Event Hubs 流提供程序

<a id="introduction"></a>

## 简介
Microsoft Orleans 的 Azure Event Hubs 流提供程序使 Orleans 应用程序能够利用 Azure Event Hubs 进行可靠、可扩展的事件处理。借助此提供程序，可以使用 Event Hubs 作为 Orleans 应用程序的流式传输基础设施，以生产和消费事件流。

<a id="getting-started"></a>

## 入门
通过 NuGet 安装此包：

```shell
dotnet add package Microsoft.Orleans.Streaming.EventHubs
```

<a id="example---configuring-event-hubs-stream-provider"></a>

## 示例：配置 Event Hubs 流提供程序

```csharp
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;
using Orleans.Hosting;
namespace ExampleGrains;

var builder = Host.CreateApplicationBuilder(args)
    .UseOrleans(siloBuilder =>
    {
        siloBuilder
            .UseLocalhostClustering()
            // Configure Azure Event Hubs as a stream provider
            .AddEventHubStreams(
                "EventHubStreamProvider",
                configurator => 
                {
                    configurator.ConfigureEventHub(builder => builder.Configure(options => 
                    {
                        options.ConnectionString = "YOUR_EVENT_HUB_CONNECTION_STRING";
                        options.ConsumerGroup = "YOUR_CONSUMER_GROUP"; // Default is "$Default"
                        options.Path = "YOUR_EVENT_HUB_NAME";
                    }));
                    
                    configurator.UseAzureTableCheckpointer(builder => builder.Configure(options => 
                    {
                        options.ConnectionString = "YOUR_STORAGE_CONNECTION_STRING";
                        options.TableName = "EventHubCheckpoints"; // Optional
                    }));
                });
    });

// Run the host
await builder.RunAsync();
```

<a id="using-orleans-grain-storage-for-checkpoints"></a>

### 使用 Orleans Grain 存储保存检查点

为兼容现有部署，Azure Table Storage 仍是默认检查点存储。也可以选择使用 Orleans Grain 和已配置的 `PubSubStore` Grain 存储提供程序来存储检查点：

```csharp
siloBuilder
    .AddMemoryGrainStorage("PubSubStore")
    .AddEventHubStreams("EventHubStreamProvider", configurator =>
    {
        configurator.ConfigureEventHub(builder => builder.Configure(options =>
        {
            options.ConnectionString = "YOUR_EVENT_HUB_CONNECTION_STRING";
            options.ConsumerGroup = "YOUR_CONSUMER_GROUP";
            options.Path = "YOUR_EVENT_HUB_NAME";
        }));
        configurator.UseGrainCheckpointer(builder => builder.Configure(options =>
        {
            options.PersistInterval = TimeSpan.FromSeconds(30);
        }));
    });
```

基于 Grain 的检查点记录器按数值顺序比较 Event Hubs 偏移量，确保较旧的偏移量无法覆盖较新的检查点。在生产环境中应配置持久化的 `PubSubStore` 提供程序；内存 Grain 存储无法在集群重启后保留检查点。切换检查点存储不会迁移现有偏移量，并可能导致事件被重放。
设置 `GrainStreamQueueCheckpointerOptions.StorageProviderName` 可使用另一个已注册的 Grain 存储提供程序。

`UseGrainCheckpointer` 和 `UseAzureTableCheckpointer` 都是 `ISiloPersistentStreamConfigurator` 的扩展方法，因此也可与其他持久化流提供程序一起使用。Event Hubs 默认配置数值型检查点排序；其他提供程序可根据自己的检查点格式设置相应的 `CheckpointComparer` 选项。

<a id="example---using-event-hub-streams-in-a-grain"></a>

## 示例：在 Grain 中使用 Event Hubs 流

```csharp
// Grain interface
public interface IStreamProcessingGrain : IGrainWithGuidKey
{
    Task StartProcessing();
}

// Grain implementation
public class StreamProcessingGrain : Grain, IStreamProcessingGrain
{
    private IStreamProvider _streamProvider;
    private IAsyncStream<MyEvent> _stream;
    private StreamSubscriptionHandle<MyEvent> _subscription;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        // Get the stream provider
        _streamProvider = GetStreamProvider("EventHubStreamProvider");
        
        // Get a reference to a specific stream
        _stream = _streamProvider.GetStream<MyEvent>(this.GetPrimaryKey(), "MyStreamNamespace");
        
        await base.OnActivateAsync(cancellationToken);
    }

    public async Task StartProcessing()
    {
        // Subscribe to the stream to process events
        _subscription = await _stream.SubscribeAsync(OnNextAsync);
    }

    private Task OnNextAsync(MyEvent evt, StreamSequenceToken token)
    {
        Console.WriteLine($"Received event: {evt.Data}");
        return Task.CompletedTask;
    }

    // Produce an event to the stream
    public Task SendEvent(MyEvent evt)
    {
        return _stream.OnNextAsync(evt);
    }
}

// Event class

public class MyEvent
{
    public string Data { get; set; }
}
```

<a id="documentation"></a>

## 文档
有关更详细的文档，请参阅：
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Orleans 流](https://dotnet.github.io/orleans/docs/streaming/)
- [Event Hubs 集成](https://dotnet.github.io/orleans/docs/implementation/streams-implementation/)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如果遇到问题或希望提供反馈，请[在 GitHub 上提交 Issue](https://github.com/dotnet/orleans/issues)。
- 欢迎加入我们的 [Discord](https://aka.ms/orleans-discord) 社区。
- 关注 Twitter 账号 [@msftorleans](https://twitter.com/msftorleans)，获取 Orleans 公告。
- 欢迎参与贡献！请先阅读[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)。
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)。