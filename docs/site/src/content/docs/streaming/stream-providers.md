---
title: "Orleans 流流提供者"
description: "按耐久性、可逆性、状态和先决条件比较内建 Orleans 流提供者。"
ms.date: 08/18/2026
ms.topic: concept-article
---

<a id="orleans-stream-providers"></a>

# Orleans 流流提供者

流体提供者将 Orleans 流 API 连接到运输,并定义其运行时间语义。从所需的耐久性、重播、吞吐量、主机环境和业务所有权中选择一个提供者。 提供者的行为仍然取决于保留、复制和可见度超时等支持服务配置。

<a id="provider-matrix"></a>

## 提供方矩阵

| 提供商 | 套包 | 状况状况现状状况现况 | 外部活动的耐久性 | 可逆 | 外部先决条件 |
|---|---|---|---|---|---|
| 内存 | [`Microsoft.Orleans.Streaming`](https://www.nuget.org/packages/Microsoft.Orleans.Streaming) | 稳定 | 否; silo 内存仅 | 是的,在瞬间记忆缓存内 | 无无无无 |
| Azure 队列存储 | [`Microsoft.Orleans.Streaming.AzureStorage`](https://www.nuget.org/packages/Microsoft.Orleans.Streaming.AzureStorage) | 稳定 | 是, Azure 存储队列 | 无 | Azure 存储账户或Azurite; 证书和稳定 Orleans 服务ID |
| Azure 活动枢纽 | [`Microsoft.Orleans.Streaming.EventHubs`](https://www.nuget.org/packages/Microsoft.Orleans.Streaming.EventHubs) | 稳定 | 是,在活动枢纽保留范围内 | 是 是 | 活动枢纽中心命名空间、枢枢纽中心、消费者群体和检查站储存 |
| 亚马逊金银 | [`Microsoft.Orleans.Streaming.Kinesis`](https://www.nuget.org/packages/Microsoft.Orleans.Streaming.Kinesis) | 稳定 | 是,在Kinesis保留范围内 | 是 是 | Kinesis数据流、AWS证书、区域和持久检查站储存 |
| 亚马逊 SQS | [`Microsoft.Orleans.Streaming.SQS`](https://www.nuget.org/packages/Microsoft.Orleans.Streaming.SQS) | 稳定 | 是,在保留SQS范围内 | 无 | AWS 账户、 队列权限、 区域/ 端点配置 |
| ADO.NET | [`Microsoft.Orleans.Streaming.AdoNet`](https://www.nuget.org/packages/Microsoft.Orleans.Streaming.AdoNet) | **阿尔法** | 是,在到期/死亡信到期前的相关表格中 | 无 | 支持的数据库、 ADO.NET 驱动程序以及 Orleans 流流 SQL schema |
| 海军 JetStream | [`Microsoft.Orleans.Streaming.NATS`](https://www.nuget.org/packages/Microsoft.Orleans.Streaming.NATS) | **阿尔法** | 可配置; 文件存储为默认 | 无 | 具有 JetStream 和充分储存的NATS服务器;主题/流管理 |
| Redis 流 | [`Microsoft.Orleans.Streaming.Redis`](https://www.nuget.org/packages/Microsoft.Orleans.Streaming.Redis) | **阿尔法** | 可通过 Redis 持久性和流流留存加以配置 | 是,但仍有条目 | Redis 部署、持久性/人道主义/人道主义政策以及保留量的定分 |

Alpha 软件包有一个 `alpha.1` 版本后缀。 将它们的API 和操作行为作为预释放、 验证装入中的失败模式以及刻意输入版本 。

Kafka 和 Azure Service Bus 并非内置的 Orleans 流源提供商。 通过应用程序代码或定制的持久性流队列适配器将它们整合, 而不是配置一个不存在的内建提供商 。

<a id="memory-streams"></a>

## 内存流

注册存储流 <xref:Orleans.Hosting.SiloBuilderMemoryStreamExtensions.AddMemoryStreams*>。 它们使用 silo 内存来排队和缓存, 因此事件不会幸存集损失。 只有在相关事件仍留在现场模拟缓存时, 才会倒转工作 。 在明确可以接受损失的情况下, 请使用此提供者进行本地开发、测试和工作量 。

见 [Tunne 内存流解结队批量](streaming-operations.md#tune-memory-stream-dequeue-batches) ,以配置每个提供者的批次计算,并平衡收费间接费用与响应规模。

<a id="azure-queue-storage"></a>

## Azure 队列存储

<a id="azure-queue-aq-stream-provider"></a>

注册 Azure 以 <xref:Orleans.Hosting.SiloBuilderExtensions.AddAzureQueueStreams*>的队列排流。 供应商使用多个 [Azure 队列存储](https://learn.microsoft.com/azure/storage/queues/storage-queues-introduction) 队列和长流拉动剂。 它不可倒转, 而 Azure 队列可以在失败后产生重复或重新订购交付。

配置当前方 <xref:Azure.Storage.Queues.QueueServiceClient> 直接在 <xref:Orleans.Configuration.AzureQueueOptions>. When <xref:Orleans.Configuration.AzureQueueOptions.QueueNames> 被解开 Orleans 从 Orleans 服务 ID 、 服务提供商名和 队列 ID 。 保持服务 ID 和 提供者名在重开时保持稳定 。 仅在您需要管理现有队列表层的时候, 明确设置队列名, 并且将这些名称保存账户的组别保持独有。

<a id="managed-identity"></a>

### 管理身份

:::code language="csharp" source="snippets/streaming/Configuration.cs" id="azure_queue_managed_identity":::

<a id="connection-string"></a>

### 连接字符串

:::code language="csharp" source="snippets/streaming/Configuration.cs" id="azure_queue_connection_string":::

示例使用耐久 Azure 表存储用于 `PubSubStore`;单是队列耐久性并不保存明确的订阅记录。

<a id="azure-event-hubs"></a>

## Azure 活动枢纽

<a id="azure-event-hub-stream-provider"></a>

注册 [Azure 活动枢纽](https://learn.microsoft.com/azure/event-hubs/event-hubs-about) , 并有 <xref:Orleans.Hosting.SiloBuilderExtensions.AddEventHubStreams*>。事件枢纽保留和分区位置使此提供者可倒转。 配置一个消费者群体专门用于 Orleans 应用程序和耐久检查站存储。 分隔点计的物理阅读平行界限, 以及保存线的恢复可以反延到多远的界限 。

新订阅可以 [首先是在拉链代理器本地缓存中保留信件](subscription-start-positions.md)。 这个缓存本地重播让事件枢纽分区接收器和检查站保持现有位置 。

事件枢纽提供者支持一个自定义的数据调整器,用于提供商专用的电线格式。 见 [整合外部流的生产者和消费者](external-streams.md) 当非Orleans 应用程序必须发布或从同一个事件枢纽中消耗。

<a id="amazon-kinesis"></a>

## 亚马逊金银

注册 [亚马孙 Kinesis 数据流](https://docs.aws.amazon.com/streams/latest/dev/introduction.html) , 并有 <xref:Orleans.Hosting.SiloBuilderExtensions.AddKinesisStreams*>。 Kinesis 将事件与 Orleans分开处理, 供应商维持每个碎片的最后交付序列号, 这样在关闭或排队调任后可以恢复交付。 关于配置、检查站选择和业务限制, 见 [亚马逊 Kinesis 流](kinesis-streaming.md) 。

<a id="amazon-sqs"></a>

## 亚马逊 SQS

登记册登记登记册 [亚马逊 SQS](https://docs.aws.amazon.com/AWSSimpleQueueService/latest/SQSDeveloperGuide/welcome.html) 与 <xref:Orleans.Hosting.SiloBuilderExtensions.AddSqsStreams*>。标准队列提供至少一次交付,而FIFO队列则在每种情况下保留顺序 Orleans 。SQS 后 [可见度超时](https://docs.aws.amazon.com/AWSSimpleQueueService/latest/SQSDeveloperGuide/sqs-visibility-timeout.html) 当处理不被确认时, 程序不被确认。 见 [亚马逊流](sqs-streaming.md) 用于标准和FIFO配置、自定义数据适配器、许可和业务指导。

<a id="adonet-streaming-alpha"></a>

## ADO.NET 溪流(阿尔法)

登记册登记登记册 [ADO.NET](https://learn.microsoft.com/dotnet/framework/data/adonet/ado-net-overview) 流着 `AddAdoNetStreams`。安装匹配的数据库驱动程序并应用 SQL 服务器 PostgreSQL,或 MySQL 宏包源中发运的流流 smama 。 信件在相关表格中可耐久, 但过期并可以按照 `AdoNetStreamOptions`供应商是不会倒转的

<a id="nats-jetstream-streaming-alpha"></a>

## NATS JetStream 流(阿尔法)

登记册登记登记册 [海军 JetStream](https://docs.nats.io/nats-concepts/jetstream) 与 `AddNatsStreams`.提供者创建或使用 JetStream 和确定性主题分区。 文件支持的存储是默认的; 内存支持 JetStream 服务器损失中,存储是可选的,不可持久。 `NatsOptions.PartitionCount` 需要相应的服务器端流更新。 服务商不可倒带 。

串列命名空间和密钥是字节身份。 供应商将主题分隔器、通配卡、白空、 控制字符和二进制值编码为单个NATS 符号,同时保留有效载荷的原始特性。 现有的安全 UTF-8 类代号保留了它们的主体和分割任务; `~`-预设的标本和字面命名空间 `null` 使用编码格式。当升级原始主题路径时,暂停制作者、排水可发送信息、更新所有提供者 silos 和客户端,然后恢复出版。 现有的分割式信息仍然可以使用,而以前保留的未分割信件需要通过升级的供应商重播。

<a id="redis-streams-streaming-alpha"></a>

## Redis 流流(阿尔法)

登记册登记登记册 [Redis 流](https://redis.io/docs/latest/develop/data-types/streams/) 与 `AddRedisStreams`供应商储存了在 Redis 并且可以重新显示,而条目则保留。 Redis 持久性取决于它 [持久性](https://redis.io/docs/latest/operate/oss_and_stack/management/persistence/) 和复制配置。 `RedisStreamingOptions.MaxStreamLength` 能够约束保留;没有它,流长是无限制的,因此需要能力规划。

<a id="custom-adapters"></a>

## 自定义适配器

<a id="queue-adapters"></a>

A [持久性流数据调整器](data-adapters.md) 定制 Azure Quue 存储、 Azure 事件枢纽或亚马逊 SQS 所使用的电线格式,同时保留该供应商的运输、分割、确认、缓存和回收行为。

<xref:Orleans.Providers.Streams.Common.PersistentStreamProvider> hosts providers built on <xref:Orleans.Streams.IQueueAdapter>. A custom queue adapter supplies enqueue/dequeue behavior, queue mapping, rewindability, and failure handling while Orleans supplies pulling agents, subscription routing, and caches. See [Write a custom persistent-stream queue adapter](custom-queue-adapter.md) for an implementation guide and [stream implementation architecture](../implementation/streams-implementation/index.md) for the runtime design.
