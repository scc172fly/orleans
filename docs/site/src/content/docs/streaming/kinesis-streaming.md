---
title: 使用 Amazon Kinesis 进行流处理
description: 为 Orleans 配置 Amazon Kinesis Data Streams，包括持久化的 DynamoDB 检查点。
ms.date: 08/07/2026
ms.topic: how-to
---

<a id="stream-with-amazon-kinesis"></a>

# 使用 Amazon Kinesis 进行流处理

[`Microsoft.Orleans.Streaming.Kinesis`](https://www.nuget.org/packages/Microsoft.Orleans.Streaming.Kinesis) 包将 Orleans 持久化流连接到 [Amazon Kinesis Data Streams](https://docs.aws.amazon.com/streams/latest/dev/introduction.html)。每个 Kinesis 分片对应一个 Orleans 队列，因此开放分片的数量限制了物理读取的并行度。Kinesis 保留期限决定消费者可以回溯重放的范围。

启动 Orleans 之前创建 Kinesis 数据流。提供程序会发现数据流的分片，但不会创建、删除、拆分或合并数据流。

<a id="configure-the-silo"></a>

## 配置 Silo

安装包并注册具名提供程序：

:::code language="csharp" source="../snippets/compiled/Streaming/KinesisSnippets.cs" id="kinesis_hosting_using":::
:::code language="csharp" source="../snippets/compiled/Streaming/KinesisSnippets.cs" id="configure_kinesis_silo":::

`PubSubStore` 持久化显式 Orleans 流订阅。检查点表具有不同的用途：它记录各分片上最后已投递的 Kinesis 序列号。

为通过该提供程序发布消息的每个 Orleans 客户端配置相同的提供程序名称、流名称和区域：

:::code language="csharp" source="../snippets/compiled/Streaming/KinesisSnippets.cs" id="configure_kinesis_client":::

未配置显式凭据时，提供程序使用 [AWS SDK for .NET 凭据解析链](https://docs.aws.amazon.com/sdk-for-net/v4/developer-guide/creds-assign.html)。生产环境优先使用 IAM 角色等工作负载凭据。使用自定义 Kinesis 兼容端点时设置 <xref:Orleans.Streaming.Kinesis.KinesisStreamOptions.Service>。

<a id="choose-checkpoint-storage"></a>

## 选择检查点存储

Kinesis 分片迭代器是临时的。因此，Orleans 在 Kinesis 外部存储最后已投递的序列号，并使用该序列号在重新启动或队列重新分配后恢复处理。

<a id="dynamodb-table-checkpoints"></a>

### DynamoDB 表检查点

调用 <xref:Orleans.Hosting.SiloKinesisStreamConfigurator.UseDynamoDBCheckpointer*> 可直接在 DynamoDB 中存储检查点。该检查点管理器：

- 为每个 Orleans 服务、提供程序和 Kinesis 分片的组合使用一条带版本的记录。
- 使用一致性读取及条件写入，防止先前的队列所有者覆盖较新的检查点。
- 默认创建按需容量表。若由基础设施配置流程负责创建该表，则将 <xref:Orleans.Configuration.DynamoDBStreamQueueCheckpointerOptions.CreateIfNotExists> 设置为 `false`。
- 使用 <xref:Orleans.Configuration.DynamoDBStreamQueueCheckpointerOptions.PersistInterval> 限制写入频率。较短的间隔可减少故障后的重放，但会增加 DynamoDB 写入流量。

当表使用预置容量模式时，设置 <xref:Orleans.Configuration.DynamoDBStreamQueueCheckpointerOptions.UseProvisionedThroughput>、<xref:Orleans.Configuration.DynamoDBStreamQueueCheckpointerOptions.ReadCapacityUnits> 和 <xref:Orleans.Configuration.DynamoDBStreamQueueCheckpointerOptions.WriteCapacityUnits>。

<a id="grain-checkpoints"></a>

### Grain 检查点

如果没有选择检查点管理器，则提供程序使用由 Orleans Grain 支持的检查点。检查点 Grain 默认使用 `PubSubStore`，因此在生产环境中必须确保该提供程序具有持久性：

:::code language="csharp" source="../snippets/compiled/Streaming/KinesisSnippets.cs" id="kinesis_streams_using":::
:::code language="csharp" source="../snippets/compiled/Streaming/KinesisSnippets.cs" id="configure_grain_checkpoints":::

两种实现均保证 Kinesis 序列号单调递增，并且在非正常关闭后可能重放少量已投递记录。消费者必须能够容忍重复投递。

<a id="operations-and-permissions"></a>

## 运维与权限

仅向应用程序授予其配置所需的 Kinesis 数据平面及 DynamoDB 表权限。Kinesis 提供程序列出分片、获取分片迭代器、读取记录和写入记录。由提供程序管理的检查点表还需要描述及创建表，以及读取和条件写入检查点记录的权限。

将 Kinesis 迭代器存留时长、读取限流、预置吞吐量及保留期限与 [Orleans 流处理指标](streaming-operations.md#observe-health) 一并监控。<xref:Orleans.Streaming.Kinesis.KinesisStreamOptions.GetRecordsInterval> 默认采用 Kinesis 为每个分片允许的最短间隔。

不支持在线重新分片。如果提供程序运行期间分片拓扑发生变化，接收器将停止，以避免队列所有权不正确的风险。拆分或合并分片后重新启动 Orleans 流提供程序。

若要查看使用 DynamoDB 实现集群成员管理、Grain 状态存储、提醒器以及 Kinesis 检查点的完整配置，请参见 [AWS Kinesis 和 DynamoDB 示例](https://github.com/dotnet/orleans/tree/main/samples/AWS/KinesisDynamoDB)。
