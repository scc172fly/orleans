<a id="microsoft-orleans-streaming-for-amazon-kinesis"></a>

# Microsoft Orleans 亚马逊 Kinesis 流

Microsoft Orleans 亚马逊 Kinesis的流提供一条由亚马逊 Kinesis 数据流支持的持久性溪流提供者。

<a id="getting-started"></a>

## 入门

从 NuGet安装软件包:

```shell
dotnet add package Microsoft.Orleans.Streaming.Kinesis
```

配置 silo上的提供者:

```csharp
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.UseOrleans(siloBuilder =>
{
    siloBuilder
        .UseLocalhostClustering()
        .AddMemoryGrainStorage("PubSubStore")
        .AddKinesisStreams(
            name: "Kinesis",
            configureOptions: options =>
            {
                options.StreamName = "orders";
                options.Region = "us-east-1";
            });
});

await builder.RunAsync();
```

配置客户端, 使用相同的提供商名和 Kinesis 选项发布到流中 :

```csharp
clientBuilder.AddKinesisStreams(
    name: "Kinesis",
    configureOptions: options =>
    {
        options.StreamName = "orders";
        options.Region = "us-east-1";
    });
```

当访问和密钥未配置时, 供应商使用标准的 AWS SDK 证书链 。

<a id="checkpoint-persistence"></a>

## 检查站持久性

Kinesis 碎片代号临时识别职位, 但Kinesis并不储存已承诺的消费者冲销。 消费者必须单独维持最后处理过的序列编号, 在关闭或重新配置后恢复 。

默认 silo 配置为此使用 Orleans grains 。每个碎片的序列号都通过配置好的 `PubSubStore` grain 存储提供者存储。这一设计不需要 DynamoDB 或Kinesis客户端库,也不允许任何 Orleans grain 存储提供者进入持续设置的检查站。

配置一个生产中的耐用 `PubSubStore` 提供商。上面显示的模拟提供商仅适合开发,因为其检查站无法在集群重新启动后生存。
设置 `GrainStreamQueueCheckpointerOptions.StorageProviderName` 使用另一个已注册的 grain 存储提供者。

grain 检查站防止序列号向后移动,并默认情况下最多每分钟写一次。使用配置超载来更改持久性间隔:

```csharp
using Orleans.Streams;

siloBuilder.AddKinesisStreams("Kinesis", configurator =>
{
    configurator.ConfigureKinesis(options => options.Configure(kinesis =>
    {
        kinesis.StreamName = "orders";
        kinesis.Region = "us-east-1";
    }));
    configurator.UseGrainCheckpointer(options => options.Configure(checkpointer =>
    {
        checkpointer.CheckpointComparer = StreamCheckpointComparers.Numeric;
        checkpointer.PersistInterval = TimeSpan.FromSeconds(30);
    }));
});
```

直接在 DynamoDB 内设置检查站,而不配置储存的 grain ,请选择 DynamoDB 表检查器:

```csharp
siloBuilder.AddKinesisStreams("Kinesis", configurator =>
{
    configurator.ConfigureKinesis(options => options.Configure(kinesis =>
    {
        kinesis.StreamName = "orders";
        kinesis.Region = "us-east-1";
    }));
    configurator.UseDynamoDBCheckpointer(options =>
    {
        options.Service = "us-east-1";
        options.TableName = "OrleansStreamCheckpoints";
    });
});
```

下 DynamoDB 使用点用计费并默认创建其表格。 它存储每服务、 服务提供商和 shard 的一行版本。 条件性写作防止了一个老化 silo 设置 `CreateIfNotExists` 至 `false` 单列表格时。

提供不同的检查站实施,使用配置超载和呼叫
`ConfigureCheckpointer<TOptions>` 和 A 相 `IStreamQueueCheckpointerFactory`.

<a id="documentation"></a>

## 文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件

- [Microsoft Orleans 文件文件](https://dotnet.github.io/orleans/docs/)
- [Orleans 流](https://dotnet.github.io/orleans/docs/streaming/)
- [Amazon Kinesis 数据流文档](https://docs.aws.amazon.com/streams/latest/dev/introduction.html)
