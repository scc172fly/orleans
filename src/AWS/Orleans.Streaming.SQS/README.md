<a id="microsoft-orleans-streaming-for-amazon-sqs"></a>

# Microsoft Orleans Amazon SQS 流提供程序

`Microsoft.Orleans.Streaming.SQS` 包将 Orleans 持久化流连接到 Amazon Simple Queue Service (SQS)。它支持标准队列和 FIFO 队列、可配置的队列分区及接收行为，以及用于应用程序自定义传输格式的数据适配器。

<a id="install"></a>

## 安装

```shell
dotnet add package Microsoft.Orleans.Streaming.SQS
```

<a id="configure"></a>

## 配置

在 Silo 和每个通过该提供程序发布消息的 Orleans 客户端上注册同名提供程序：

```csharp
siloBuilder.AddSqsStreams("Orders", options =>
{
    options.ConnectionString = "Service=us-east-1";
    options.ReceiveWaitTimeSeconds = 20;
    options.VisibilityTimeoutSeconds = 60;
});

clientBuilder.AddSqsStreams("Orders", options =>
{
    options.ConnectionString = "Service=us-east-1";
});
```

连接字符串中没有显式凭据时，提供程序使用 AWS SDK 凭据解析链。优先使用 IAM 角色等工作负载凭据。

<a id="documentation"></a>

## 文档

有关 FIFO 队列、自定义数据适配器、投递语义、权限、调优及运维指导，请参阅[使用 Amazon SQS 处理流](https://dotnet.github.io/orleans/docs/streaming/sqs-streaming/)。

<a id="feedback-and-contributing"></a>

## 反馈与贡献

- [创建 Orleans Issue](https://github.com/dotnet/orleans/issues)
- [加入 Discord 上的 Orleans 社区](https://aka.ms/orleans-discord)
- 阅读[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
