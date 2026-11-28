---
title: Orleans 流提供程序
description: 按持久性、回溯能力、发布状态和前置条件比较 Orleans 内置流提供程序。
ms.date: 08/18/2026
ms.topic: concept-article
---

<a id="orleans-stream-providers"></a>

# Orleans 流提供程序

流提供程序将 Orleans 流处理 API 连接到传输系统，并定义其运行时语义。应根据所需的持久性、重放能力、吞吐量、托管环境及运维责任选择提供程序。提供程序行为仍取决于底层服务的配置，例如保留期限、复制和可见性超时。

<a id="provider-matrix"></a>

## 提供程序对照表

| 提供程序 | 包 | 状态 | 外部事件持久性 | 可回溯 | 外部前置条件 |
|---|---|---|---|---|---|
| 内存 | [`Microsoft.Orleans.Streaming`](https://www.nuget.org/packages/Microsoft.Orleans.Streaming) | 稳定 | 否；仅存储在 Silo 内存中 | 是，限于临时内存缓存 | 无 |
| Azure Queue Storage | [`Microsoft.Orleans.Streaming.AzureStorage`](https://www.nuget.org/packages/Microsoft.Orleans.Streaming.AzureStorage) | 稳定 | 是，保存在 Azure Storage 队列中 | 否 | Azure Storage 帐户或 Azurite；凭据及稳定的 Orleans 服务 ID |
| Azure Event Hubs | [`Microsoft.Orleans.Streaming.EventHubs`](https://www.nuget.org/packages/Microsoft.Orleans.Streaming.EventHubs) | 稳定 | 是，限于 Event Hubs 保留期限 | 是 | Event Hubs 命名空间、事件中心、消费者组及检查点存储 |
| Amazon Kinesis | [`Microsoft.Orleans.Streaming.Kinesis`](https://www.nuget.org/packages/Microsoft.Orleans.Streaming.Kinesis) | 稳定 | 是，限于 Kinesis 保留期限 | 是 | Kinesis 数据流、AWS 凭据、区域及持久化检查点存储 |
| Amazon SQS | [`Microsoft.Orleans.Streaming.SQS`](https://www.nuget.org/packages/Microsoft.Orleans.Streaming.SQS) | 稳定 | 是，限于 SQS 保留期限 | 否 | AWS 帐户、队列权限、区域或端点配置 |
| ADO.NET | [`Microsoft.Orleans.Streaming.AdoNet`](https://www.nuget.org/packages/Microsoft.Orleans.Streaming.AdoNet) | **Alpha** | 是，保存在关系数据库表中，直至过期或死信被淘汰 | 否 | 受支持的数据库、ADO.NET 驱动程序及 Orleans 流处理 SQL 架构 |
| NATS JetStream | [`Microsoft.Orleans.Streaming.NATS`](https://www.nuget.org/packages/Microsoft.Orleans.Streaming.NATS) | **Alpha** | 可配置；默认使用文件存储 | 否 | 启用 JetStream 的 NATS 服务器及充足存储；主题和流管理 |
| Redis Streams | [`Microsoft.Orleans.Streaming.Redis`](https://www.nuget.org/packages/Microsoft.Orleans.Streaming.Redis) | **Alpha** | 通过 Redis 持久化和流保留策略配置 | 是，限于条目仍存在的范围 | Redis 部署、持久化与高可用策略及保留容量规划 |

Alpha 包带有 `alpha.1` 版本后缀。将其 API 及运维行为视为预发布功能，在负载下验证故障模式，并明确锁定版本。

Kafka 和 Azure Service Bus 并非 Orleans 内置流提供程序。应通过应用程序代码或自定义持久化流队列适配器进行集成，而不是配置不存在的内置提供程序。

<a id="memory-streams"></a>

## 内存流

使用 <xref:Orleans.Hosting.SiloBuilderMemoryStreamExtensions.AddMemoryStreams*> 注册内存流。它使用 Silo 内存存储队列和缓存，因此事件无法在集群丢失后保留。仅当相关事件仍位于存活的内存缓存中时，才能回溯。将此提供程序用于本地开发、测试以及明确可以接受数据丢失的工作负载。

参见[调优内存流出队批次](streaming-operations.md#tune-memory-stream-dequeue-batches)，了解如何为每个提供程序配置批次数量，并在调用开销与响应大小之间权衡。

<a id="azure-queue-storage"></a>

## Azure Queue Storage

<a id="azure-queue-aq-stream-provider"></a>

使用 <xref:Orleans.Hosting.SiloBuilderExtensions.AddAzureQueueStreams*> 注册 Azure Queue 流。该提供程序使用多个 [Azure Queue Storage](https://learn.microsoft.com/azure/storage/queues/storage-queues-introduction) 队列以及持久化流拉取代理。它不支持回溯，Azure Queue 重试可能产生重复消息或使故障后的投递顺序发生变化。

直接配置当前的 <xref:Azure.Storage.Queues.QueueServiceClient>，具体在 <xref:Orleans.Configuration.AzureQueueOptions> 上进行配置。如果未设置 <xref:Orleans.Configuration.AzureQueueOptions.QueueNames>，Orleans 会根据 Orleans 服务 ID、提供程序名称和队列 ID 生成名称。保持服务 ID 和提供程序名称在重启之间稳定。仅当需要管理现有队列拓扑时才显式设置队列名称，并确保共享存储帐户的各集群之间的名称唯一。

<a id="managed-identity"></a>

### 托管标识

:::code language="csharp" source="snippets/streaming/Configuration.cs" id="azure_queue_managed_identity":::

<a id="connection-string"></a>

### 连接字符串

:::code language="csharp" source="snippets/streaming/Configuration.cs" id="azure_queue_connection_string":::

示例为 `PubSubStore` 使用持久化 Azure Table Storage；仅有队列持久性无法保留显式订阅记录。

<a id="azure-event-hubs"></a>

## Azure Event Hubs

<a id="azure-event-hub-stream-provider"></a>

注册 [Azure Event Hubs](https://learn.microsoft.com/azure/event-hubs/event-hubs-about) 时使用 <xref:Orleans.Hosting.SiloBuilderExtensions.AddEventHubStreams*>。Event Hubs 的保留期限和分区位置使此提供程序支持回溯。配置 Orleans 应用程序专用的消费者组及持久化检查点存储。分区数量限制物理读取并行度，保留期限限制恢复时可回溯的范围。

新订阅可以[从拉取代理本地缓存保留的消息开始](subscription-start-positions.md)。这种缓存本地重放不会改变 Event Hubs 分区接收器及检查点的当前位置。

Event Hubs 提供程序支持用于自定义传输格式的数据适配器。当非 Orleans 应用程序必须向同一 Event Hub 发布消息或从中消费消息时，请参见[集成外部流生产者和消费者](external-streams.md)。

<a id="amazon-kinesis"></a>

## Amazon Kinesis

注册 [Amazon Kinesis Data Streams](https://docs.aws.amazon.com/streams/latest/dev/introduction.html) 时使用 <xref:Orleans.Hosting.SiloBuilderExtensions.AddKinesisStreams*>。Kinesis 独立于 Orleans 保留事件，提供程序持久化每个分片上最后已投递的序列号，以便在关闭或队列重新分配后恢复投递。有关配置、检查点选择及运维限制，参见[使用 Amazon Kinesis 进行流处理](kinesis-streaming.md)。

<a id="amazon-sqs"></a>

## Amazon SQS

注册 [Amazon SQS](https://docs.aws.amazon.com/AWSSimpleQueueService/latest/SQSDeveloperGuide/welcome.html) 时使用 <xref:Orleans.Hosting.SiloBuilderExtensions.AddSqsStreams*>。标准队列提供至少一次投递，而 FIFO 队列保持每个 Orleans 流内部的顺序。如果处理未获确认，则 SQS 在[可见性超时](https://docs.aws.amazon.com/AWSSimpleQueueService/latest/SQSDeveloperGuide/sqs-visibility-timeout.html) 后重新投递。有关标准及 FIFO 配置、自定义数据适配器、权限和运维指导，参见[使用 Amazon SQS 进行流处理](sqs-streaming.md)。

<a id="adonet-streaming-alpha"></a>

## ADO.NET 流处理（Alpha）

注册 [ADO.NET](https://learn.microsoft.com/dotnet/framework/data/adonet/ado-net-overview) 流处理时使用 `AddAdoNetStreams`。安装匹配的数据库驱动程序，并应用包源码随附的 SQL Server、PostgreSQL 或 MySQL 流处理架构。消息持久化保存在关系数据库表中，但根据 `AdoNetStreamOptions` 可能过期并转移到死信中。该提供程序不支持回溯。

<a id="nats-jetstream-streaming-alpha"></a>

## NATS JetStream 流处理（Alpha）

注册 [NATS JetStream](https://docs.nats.io/nats-concepts/jetstream) 时使用 `AddNatsStreams`。提供程序创建或使用 JetStream 流及确定性的主题分区。默认使用文件存储；可选的内存型 JetStream 存储无法在服务器丢失后持久保留数据。修改 `NatsOptions.PartitionCount` 时需要相应更新服务端流。该提供程序不支持回溯。

流命名空间和键是以字节表示的标识。提供程序将主题分隔符、通配符、空白字符、控制字符和二进制值编码为各个 NATS 令牌，同时在有效载荷中保留原始标识。现有安全 UTF-8 令牌保留其主题和分区分配；以前缀 `~` 开头的令牌和字面命名空间 `null` 使用编码形式。从原始主题路由升级时，暂停生产者，处理完可投递消息，升级所有使用此提供程序的 Silo 和客户端，然后恢复发布。已有分区消息仍可消费，先前保留的未分区消息需要通过升级后的提供程序重放。

<a id="redis-streams-streaming-alpha"></a>

## Redis Streams 流处理（Alpha）

注册 [Redis Streams](https://redis.io/docs/latest/develop/data-types/streams/) 时使用 `AddRedisStreams`。提供程序在 Redis 中存储事件及检查点，并在条目仍存在时支持回溯。Redis 的持久性取决于其[持久化](https://redis.io/docs/latest/operate/oss_and_stack/management/persistence/) 和复制配置。`RedisStreamingOptions.MaxStreamLength` 可以限制保留范围；若未设置，流长度不受限制，因此需要进行容量规划。

<a id="custom-adapters"></a>

## 自定义适配器

<a id="queue-adapters"></a>

[持久化流数据适配器](data-adapters.md) 可自定义 Azure Queue Storage、Azure Event Hubs 或 Amazon SQS 使用的传输格式，同时保留对应提供程序的传输、分区、确认、缓存及恢复行为。

<xref:Orleans.Providers.Streams.Common.PersistentStreamProvider> 托管基于 <xref:Orleans.Streams.IQueueAdapter> 构建的提供程序。自定义队列适配器提供入队和出队行为、队列映射、回溯能力及故障处理，而 Orleans 提供拉取代理、订阅路由及缓存。有关实现指导，参见[编写自定义持久化流队列适配器](custom-queue-adapter.md)；有关运行时设计，参见[流处理实现架构](../implementation/streams-implementation/index.md)。
