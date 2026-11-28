---
title: 自定义持久化流数据格式
description: 为带版本的队列消息契约配置 Orleans 持久化流数据适配器。
ms.date: 08/18/2026
ms.topic: how-to
---

<a id="customize-persistent-stream-data-formats"></a>

# 自定义持久化流数据格式

持久化流数据适配器负责在 Orleans 事件批次和提供程序的原生队列消息之间转换。它负责传输格式契约：流标识、事件类型和有效载荷编码、架构版本，以及跨越传输边界的请求上下文值。提供程序的队列适配器仍负责传输访问、分区分配、接收确认、缓存及恢复。

当传输方式不变，而应用程序需要带版本的有效载荷格式、需要消费在 Orleans 外部生产的消息，或者需要发布外部消费者能够解码的消息时，请使用数据适配器。当 Orleans 还需要新的传输方式或不同的队列语义时，请使用[自定义队列适配器](custom-queue-adapter.md)。

<a id="choose-the-provider-extension-point"></a>

## 选择提供程序的扩展点

内置提供程序公开以下数据适配器扩展点：

| 提供程序 | 数据适配器契约 | 注册方式 | 运行时效果 |
|---|---|---|---|
| Azure Queue Storage | <xref:Orleans.Streams.IQueueDataAdapter`2>，使用 `string` 队列消息和 <xref:Orleans.Streams.IBatchContainer> 批次 | <xref:Orleans.Hosting.AzureQueueStreamConfiguratorExtensions.ConfigureQueueDataAdapter*> | 替换编码及解码行为，保留 Azure Queue 映射、可见性、删除及不可回溯的投递行为 |
| Azure Event Hubs | <xref:Orleans.Streaming.EventHubs.IEventHubDataAdapter> | <xref:Orleans.Hosting.EventHubStreamConfiguratorExtensions.UseDataAdapter*> | 替换传输格式、流映射及缓存转换行为，保留 Event Hubs 分区读取、检查点管理及可回溯的投递行为 |
| Amazon SQS | <xref:Orleans.Streaming.SQS.Streams.ISQSDataAdapter> | <xref:Orleans.Hosting.SiloSqsStreamConfigurator.UseDataAdapter*> 和 <xref:Orleans.Hosting.ClusterClientSqsStreamConfigurator.UseDataAdapter*> | 替换消息正文、应用程序属性及批次解码行为，保留 SQS 队列映射、FIFO 传输字段、删除及不可回溯的投递行为 |

<xref:Orleans.Streams.IQueueDataAdapter`1> 定义从 Orleans 批次到原生队列消息的转换。<xref:Orleans.Streams.IQueueDataAdapter`2> 增加从原生消息到 Orleans 投递的批次容器的转换。提供程序特定的契约可以增加其传输方式所需的位置和缓存操作。

<a id="define-a-versioned-contract"></a>

## 定义带版本的契约

独立于 CLR 程序集名称及 Orleans 序列化内部实现定义传输契约。可持久保存的消息信封通常包含：

- 架构版本；
- 流命名空间和键；
- 稳定的事件类型标识符；
- 一个或多个事件有效载荷；以及
- 一组显式定义的跨进程元数据。

将每条消息分配给且仅分配给一个 <xref:Orleans.Runtime.StreamId>。保持流 ID 到物理分区的映射稳定，以使同一流中的事件保持提供程序的排序行为。适配器接收批次，因此消息信封必须保留每个批次中的事件顺序。

将请求上下文视为显式契约。示例携带字符串形式的关联 ID，并将进程本地值保留在生产消息的应用程序中。

<a id="implement-an-azure-queue-data-adapter"></a>

## 实现 Azure Queue 数据适配器

以下适配器写入带版本的 JSON 消息信封，并在 Azure Queue Storage 返回消息时重建 <xref:Orleans.Streams.IBatchContainer>。Azure Queue 接收器在读取时提供 `sequenceId`，批次使用它为每个事件创建接收器本地的序列令牌。

:::code language="csharp" source="snippets/streaming/StreamDataAdapters.cs" id="azure_queue_data_adapter":::

批次容器仅公开与请求的流类型兼容的事件，根据队列消息的序列号派生各事件的令牌，并导入传输格式契约定义的元数据：

:::code language="csharp" source="snippets/streaming/StreamDataAdapters.cs" id="azure_queue_batch_container":::

在使用该提供程序的每个 Silo 和 Orleans 客户端上注册相同的适配器、队列服务客户端及物理队列名称。Silo 使用适配器进行读取和写入；客户端在发布消息时使用适配器。

:::code language="csharp" source="snippets/streaming/StreamDataAdapters.cs" id="azure_queue_data_adapter_registration":::

进行此注册时同时配置持久化的 `PubSubStore` Grain 存储，参见[Orleans 流提供程序](stream-providers.md#azure-queue-storage)。

<a id="implement-an-event-hubs-data-adapter"></a>

## 实现 Event Hubs 数据适配器

对于 Event Hubs，如果 <xref:Orleans.Streaming.EventHubs.EventHubDataAdapter> 的缓存消息表示和检查点行为符合应用程序需求，则从该类型派生。重写以下方法：

- <xref:Orleans.Streaming.EventHubs.EventHubDataAdapter.GetStreamIdentity*>，将每个 `EventData` 实例映射到流；
- <xref:Orleans.Streaming.EventHubs.EventHubDataAdapter.GetBatchContainer*>，为 Orleans 消费者解码缓存中的有效载荷；
- <xref:Orleans.Streaming.EventHubs.EventHubDataAdapter.ToQueueMessage*>，对通过 Orleans 发布的事件进行编码；以及
- <xref:Orleans.Streaming.EventHubs.EventHubDataAdapter.GetPartitionKey*>，选择物理 Event Hubs 分区键。

适配器还通过 <xref:Orleans.Streaming.EventHubs.IEventHubDataAdapter> 参与缓存转换及序列位置定位。在构造批次令牌时保留 Event Hubs 偏移量和序列号，使检查点及回溯行为与分区日志保持一致。

在 Silo 和发布消息的客户端上，以相同提供程序名称注册适配器及 Event Hubs 连接。Silo 注册还配置持久化的 Azure Table 检查点：

:::code language="csharp" source="snippets/streaming/StreamDataAdapters.cs" id="event_hub_data_adapter_registration":::

[自定义数据适配器示例](https://github.com/dotnet/orleans/tree/main/samples/Streaming/CustomDataAdapter) 展示了用于读取外部生产者 JSON 消息的 Event Hubs 适配器。端到端互操作流程参见[集成外部流生产者和消费者](external-streams.md)。

<a id="implement-an-amazon-sqs-data-adapter"></a>

## 实现 Amazon SQS 数据适配器

实现 <xref:Orleans.Streaming.SQS.Streams.ISQSDataAdapter> 以在 `Amazon.SQS.Model.Message` 和 <xref:Orleans.Streams.IBatchContainer> 之间转换。出站转换提供消息正文及应用程序定义的消息属性。提供程序分配物理队列，并在 FIFO 模式下分配消息组和去重 ID。入站转换接收 SQS 消息及接收器本地的序列号。

在每个 Silo 和发布消息的客户端上，以相同的提供程序名称注册同一适配器。为解码器所需的每个属性配置 <xref:Orleans.Configuration.SqsOptions.ReceiveMessageAttributes> 和 <xref:Orleans.Configuration.SqsOptions.ReceiveMessageSystemAttributes>。有关经过编译验证的注册示例和运维指导，参见[使用 Amazon SQS 进行流处理](sqs-streaming.md#use-an-application-wire-format)。

<a id="evolve-the-wire-contract"></a>

## 演进传输格式契约

使用扩展再收缩的发布策略：

1. 部署同时接受当前架构版本和下一架构版本的读取端。
1. 修改写入端，使其输出下一版本。
1. 保留旧版读取能力，直到队列或事件日志的保留窗口中不再包含旧版消息。
1. 核实队列深度、最早消息存留时长和检查点位置后，移除对旧版本的支持。

仅迁移有效载荷时，应保持提供程序名称、流标识编码、分区映射、队列模式和序列解释方式稳定。其中任意一项发生变化都属于流拓扑或恢复迁移，需要单独制定切换方案。

转换失败会表现为流投递失败。对于格式错误的有效载荷、不支持的版本和缺失的路由元数据，应抛出描述明确的异常，以使提供程序按其投递语义保留或重放源消息。监控转换失败，并通过传输层的运维流程隔离有毒消息，以免它们耗尽保留窗口或阻塞分区进度。

使用每个已部署架构版本保留的消息测试双向转换。测试范围应包括异构批次、请求上下文、重复投递、格式错误的消息信封、未知版本、滚动升级，以及从较早 Event Hubs 检查点进行重放。
