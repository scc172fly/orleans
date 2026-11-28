<a id="microsoft-orleans-stream-provider-for-nats"></a>

# Microsoft Orleans NATS 流提供程序

<a id="introduction"></a>

## 简介
Microsoft Orleans NATS 流提供程序使 Orleans 应用程序能够利用 NATS JetStream 进行可靠且可扩展的事件处理。该提供程序允许将 NATS JetStream 用作 Orleans 应用程序的流式处理基础设施，以生产和消费事件流。

<a id="getting-started"></a>

## 入门
要使用此包，请通过 NuGet 安装：

```shell
dotnet add package Microsoft.Orleans.Streaming.NATS
```

<a id="example---configuring-nats-stream-provider"></a>

## 示例 - 配置 NATS 流提供程序

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

## 示例 - 在客户端配置 NATS 流

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

## 示例 - 在 Grain 中使用 NATS 流

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

## 流标识与升级

提供程序将每个 `StreamId` 路由为两个 NATS subject 令牌，分别对应命名空间和键。点号、通配符、空白字符、控制字符及任意二进制字节都会编码为 `~` 后接大写十六进制字节的形式。以 `~` 开头的令牌及字面值为 `null` 的命名空间同样采用此编码，以区分编码后的值、字面值及空命名空间。其他 UTF-8 令牌保留原有 subject 和分区分配方式。JetStream 使用现有 subject 转换来选择分区，消息有效载荷则保留原始命名空间及键的字节。

消费者筛选条件、持久消费者名称及 JetStream 流配置均保持不变，因此既有分区消息可以继续被消费。提供程序读取旧版 `namespace/key` JSON 标识格式，并对能够使用这种格式无损往返转换的标识继续写入该格式。空命名空间、包含 `/` 的命名空间及非 UTF-8 标识使用双元素数组表示，两个元素分别为命名空间字节和键字节的 Base64 编码。

从原始 subject 格式升级时，请暂停生产者并排空可投递消息，升级使用该提供程序的所有 Silo 及客户端，然后恢复发布。这样可以协调发布者与消费者之间的新的有效载荷格式以及转义标识的分区分配变化。对于升级前保留在未分区 subject 上的事件（例如由带点号的命名空间产生的事件），需要通过升级后的提供程序重放，才能进入消费者分区。

<a id="documentation"></a>

## 文档
有关更全面的文档，请参阅：
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Orleans 流](https://dotnet.github.io/orleans/docs/streaming/)
- [NATS JetStream 文档](https://docs.nats.io/nats-concepts/jetstream)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如果遇到问题或希望提供反馈，请[在 GitHub 上创建 Issue](https://github.com/dotnet/orleans/issues)
- 加入我们的 [Discord](https://aka.ms/orleans-discord) 社区
- 关注 Twitter 上的 [@msftorleans](https://twitter.com/msftorleans) 账号，获取 Orleans 公告
- 欢迎贡献！请阅读我们的[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)
