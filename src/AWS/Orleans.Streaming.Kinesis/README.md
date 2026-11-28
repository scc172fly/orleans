<a id="microsoft-orleans-streaming-for-amazon-kinesis"></a>

# Microsoft Orleans Amazon Kinesis 流提供程序

Microsoft Orleans Amazon Kinesis 流提供程序以 Amazon Kinesis Data Streams 为后备服务，提供持久化流功能。

<a id="getting-started"></a>

## 入门

从 NuGet 安装此包：

```shell
dotnet add package Microsoft.Orleans.Streaming.Kinesis
```

在 Silo 上配置提供程序：

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

使用相同的提供程序名称和 Kinesis 选项配置向流发布消息的客户端：

```csharp
clientBuilder.AddKinesisStreams(
    name: "Kinesis",
    configureOptions: options =>
    {
        options.StreamName = "orders";
        options.Region = "us-east-1";
    });
```

未配置访问密钥和秘密密钥时，提供程序使用标准 AWS SDK 凭据链。

<a id="checkpoint-persistence"></a>

## 检查点持久化

Kinesis 分片迭代器临时标识某个位置，但 Kinesis 不存储已提交的消费者偏移量。消费者必须单独持久化最后处理的序列号，才能在关闭或重新分配后恢复处理。

默认 Silo 配置使用 Orleans Grain 实现这一目的。每个分片的序列号通过配置的 `PubSubStore` Grain 存储提供程序保存。此设计不需要 DynamoDB 或 Kinesis Client Library，可使用任意 Orleans Grain 存储提供程序持久化检查点。

在生产环境中请配置持久化的 `PubSubStore` 提供程序。上面示例中的内存提供程序仅适用于开发，因为集群重启后其检查点会丢失。
设置 `GrainStreamQueueCheckpointerOptions.StorageProviderName` 以使用其他已注册的 Grain 存储提供程序。

Grain 检查点组件阻止序列号回退，默认每分钟最多写入一次。使用配置器重载更改持久化间隔：

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

如需不配置 Grain 存储而直接在 DynamoDB 中持久化检查点，请选择 DynamoDB 表检查点组件：

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

DynamoDB 检查点组件采用按需计费，默认创建其表。它为每个服务、提供程序和分片的组合存储一行带版本的数据。条件写入阻止已失效的 Silo 所有者使检查点回退。如果表由外部预先配置，请将 `CreateIfNotExists` 设置为 `false`。

若要提供不同的检查点实现，请使用配置器重载并调用
`ConfigureCheckpointer<TOptions>`，传入 `IStreamQueueCheckpointerFactory`。

<a id="documentation"></a>

## 文档

- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Orleans 流](https://dotnet.github.io/orleans/docs/streaming/)
- [Amazon Kinesis Data Streams 文档](https://docs.aws.amazon.com/streams/latest/dev/introduction.html)
