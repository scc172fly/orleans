<a id="microsoft-orleans-stream-provider-for-azure-event-hubs"></a>

# Microsoft Orleans 流流供应者 Azure 活动中心

<a id="introduction"></a>

## 简介
Microsoft Orleans 事件枢纽的流提供方 Azure 活动枢纽使 Orleans 应用程序能够利用 Azure 事件枢纽进行可靠、可缩放的事件处理。该提供者允许您使用“事件枢纽”作为您 Orleans 应用的流干柱,用于生产和消费事件流。

<a id="getting-started"></a>

## 入门
要使用此软件包, 请通过 NuGet安装 :

```shell
dotnet add package Microsoft.Orleans.Streaming.EventHubs
```

<a id="example---configuring-event-hubs-stream-provider"></a>

## 示例 - 配置事件枢纽流提供方

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

### 使用 Orleans grain 储存检查站

Azure 表格储存库仍然是与现有部署兼容的默认检查站仓库。 Orleans grains 和配置 `PubSubStore` grain 存储提供者:

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

grain 检查站对事件枢纽的偏移应用数字顺序,这样旧的偏移无法覆盖更新的检查站。配置生产中的耐用 `PubSubStore` 供应商;模拟 grain 储存不会在整个集群重新启动时保持检查站。切换检查站并不迁移现有偏移,并可能导致事件重现。
设置 `GrainStreamQueueCheckpointerOptions.StorageProviderName` 使用另一个已注册的 grain 存储提供者。

`UseGrainCheckpointer` 和 `UseAzureTableCheckpointer` 扩展 `ISiloPersistentStreamConfigurator`,因此它们也可以用于其他持久性流提供者。事件枢纽配置数字检查站默认命令;其他提供者可以为其检查格式设定相应的 `CheckpointComparer` 选项。

<a id="example---using-event-hub-streams-in-a-grain"></a>

## 示例 - 在 Grain中使用事件枢纽流

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

## 文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件
关于更全面的文件,请参见:
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Orleans 流](https://dotnet.github.io/orleans/docs/streaming/)
- [活动枢纽整合](https://dotnet.github.io/orleans/docs/implementation/streams-implementation/)

<a id="feedback--contributing"></a>

## 反馈和贡献
- 如果有任何问题或希望提供反馈,请 [GitHub上的开关](https://github.com/dotnet/orleans/issues)
- 加入我们社区 [不一致](https://aka.ms/orleans-discord)
- 跟随 [@msftorleans 地图上写着:](https://twitter.com/msftorleans) 推特账户发布 Orleans 通知
- 请审查我们的 [会费捐款准则](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 该项目在 [麻省理工学院许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)下获得许可证