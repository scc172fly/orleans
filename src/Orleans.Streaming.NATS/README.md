<a id="microsoft-orleans-stream-provider-for-nats"></a>

# Microsoft Orleans NATS 流源提供商

<a id="introduction"></a>

## 简介
Microsoft Orleans NATS 启动的流源提供器 Orleans 利用NATS 应用来影响 NATS JetStream 用于可靠、可缩放的事件处理。此提供者允许您使用NATS JetStream 做你们的脊柱, Orleans 用于生产和消费事件流。

<a id="getting-started"></a>

## 入门
要使用此软件包, 请通过 NuGet安装 :

```shell
dotnet add package Microsoft.Orleans.Streaming.NATS
```

<a id="example---configuring-nats-stream-provider"></a>

## 示例 - 配置 NATS 流供应者

```csharp
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;
using Orleans.Streaming.NATS.Hosting;

var builder = Host.CreateApplicationBuilder(args)
    .UseOrleans(siloBuilder =>
    {
        siloBuilder
            .UseLocalhostClustering()
            // Configure NATS JetStream as a stream provider
            .AddNatsStreams(
                "NatsStreamProvider",
                options =>
                {
                    options.StreamName = "orleans-stream";
                    // Optional: Configure NATS client options
                    // options.NatsClientOptions = new NatsOpts { Url = "nats://localhost:4222" };
                    // Optional: Configure batch size (default: 100)
                    // options.BatchSize = 100;
                    // Optional: Configure partition count (default: 8)
                    // options.PartitionCount = 8;
                });
    });

// Run the host
await builder.RunAsync();
```

<a id="example---configuring-nats-streams-on-client"></a>

## 实例 - 配置客户端NATS流

```csharp
using Microsoft.Extensions.Hosting;
using Orleans.Streaming.NATS.Hosting;

var builder = Host.CreateApplicationBuilder(args)
    .UseOrleansClient(clientBuilder =>
    {
        clientBuilder
            .UseLocalhostClustering()
            .AddNatsStreams(
                "NatsStreamProvider",
                options =>
                {
                    options.StreamName = "orleans-stream";
                });
    });

await builder.RunAsync();
```

<a id="example---using-nats-streams-in-a-grain"></a>

## 示例 - 在 Grain中使用NATS流

```csharp
using Orleans;
using Orleans.Streams;

// Grain interface
public interface IStreamProcessingGrain : IGrainWithGuidKey
{
    Task StartProcessing();
    Task SendEvent(MyEvent evt);
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
        _streamProvider = this.GetStreamProvider("NatsStreamProvider");
        
        // Get a reference to a specific stream
        _stream = _streamProvider.GetStream<MyEvent>(
            StreamId.Create("MyStreamNamespace", this.GetPrimaryKey()));
        
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

<a id="stream-identities-and-upgrades"></a>

## 流特征和升级

提供商的路线 `StreamId` 以两个NATS主题符号为两个 NATS 主题符号,一个用于命名空间,一个用于密钥。点、通配符、通配符、白空、控制字符和任意二进制字元编码成 `~` 以大写十六进制字节后。 `~` 和字面命名空间 `null` 使用此编码, 保留与字面值和空命名空间不同的编码值。 其他 UTF-8 质点保留了它们现有的主题和分区任务 。 JetStream 应用现有对象变换来选择分区,而信件有效载荷保留原始命名空间和密钥字节。

消费者过滤器、耐久的消费名称和 JetStream 流配置保持不变,因此继续消耗现有的分隔式信件。提供者读取遗留的 `namespace/key` JSON 身份并写出该格式用于圆程中的身份。空命名空间、包含 `/`的命名空间和非 UTF-8 身份使用两个元素的基数64 和密钥字节。

升级原始主题格式时,要更新原主格式,暂停制片人和排水可发送信息,用供应商升级所有 silos 客户,然后恢复出版。这可以协调新的有效载荷格式以及针对出版商和消费者之间逃逸身份的更改分配任务。在升级前非分割主题上保留的活动,例如由虚点命名空间生成的事件,需要通过升级后提供者重新播放才能进入消费分区。

<a id="documentation"></a>

## 文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件
关于更全面的文件,请参见:
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Orleans 流](https://dotnet.github.io/orleans/docs/streaming/)
- [NATS JetStream 文档](https://docs.nats.io/nats-concepts/jetstream)

<a id="feedback--contributing"></a>

## 反馈和贡献
- 如果有任何问题或希望提供反馈,请 [GitHub上的开关](https://github.com/dotnet/orleans/issues)
- 加入我们社区 [不一致](https://aka.ms/orleans-discord)
- 跟随 [@msftorleans 地图上写着:](https://twitter.com/msftorleans) 推特账户发布 Orleans 通知
- 请审查我们的 [会费捐款准则](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 该项目在 [麻省理工学院许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)下获得许可证
