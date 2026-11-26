<a id="microsoft-orleans-streaming-for-amazon-sqs"></a>

# Microsoft Orleans 亚马逊 SQS 的流

`Microsoft.Orleans.Streaming.SQS` 套件将 Orleans 持久性流连接到亚马逊简单队列服务(SQS) 。 它支持标准队列和 FIFO 队列、可配置队列分隔和接收行为,以及自定义数据适应器,用于应用程序定义的线条格式。

<a id="install"></a>

## 安安装

```shell
dotnet add package Microsoft.Orleans.Streaming.SQS
```

<a id="configure"></a>

## 配置配置

注册处上名相同的供应商 silo 和每 Orleans 客户端通过该客户端发布:

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

当连接字符串中没有明确的证书时, 提供者使用 AWS SDK 的证明解析链。 优先选择工作量证书, 例如 IAM 角色 。

<a id="documentation"></a>

## 文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件

FIFO队列、自定义数据适配器、交付语义、许可、调试和业务指导,见 [亚马逊流](https://dotnet.github.io/orleans/docs/streaming/sqs-streaming/) 。

<a id="feedback-and-contributing"></a>

## 反馈和捐款

- [打开 Orleans 问题](https://github.com/dotnet/orleans/issues)
- [以不和的方式加入 Orleans 社区](https://aka.ms/orleans-discord)
- 审查 [会费捐款准则](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
