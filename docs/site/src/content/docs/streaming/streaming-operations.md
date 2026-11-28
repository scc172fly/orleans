---
title: 运维和调优 Orleans 流
description: 应用背压、调优持久化流提供程序，并观测 Orleans 流处理健康状况。
ms.date: 09/10/2026
ms.topic: concept-article
---

<a id="operate-and-tune-orleans-streams"></a>

# 运维和调优 Orleans 流

根据实测的滞后、吞吐量、故障和内存压力进行调优。提供程序的默认值只是起点，并非通用生产环境配置。

<a id="backpressure"></a>

## 背压

持久化流提供程序将事件拉取到 Silo 端缓存并投递给各订阅。消费者在接管数据项的处理责任之前不完成 <xref:Orleans.Streams.IAsyncObserver`1.OnNextAsync*>，以此施加背压。因此，缓慢的消费者可能增加队列中保留的数据量、缓存压力和端到端滞后。

限制消费者每个执行轮次的工作量：

- 持久化所需状态并及时完成处理。
- 将独立的长时间运行工作交由显式建模的后续流程处理。
- 避免同步阻塞和无界并行工作。
- 通过选择足够多的提供程序队列或分区并分散流键来扩展。单个热门流如果指向一个有状态 Grain，仍受该 Grain 处理速率限制。

消费者端的背压并不意味着生产者完成情况与消费进度同步。生产者的 `OnNextAsync` 报告提供程序已接受数据，而非下游消费者已追上进度。

<a id="protect-a-slow-event-hubs-consumer"></a>

### 保护缓慢的 Event Hubs 消费者

Event Hubs 提供程序为每个订阅维护独立的缓存游标，因此即使某个订阅落后，较快的订阅仍能继续。但一个 Event Hubs 分区的所有游标共享同一 Silo 端缓存。默认情况下，提供程序使用这些游标压力贡献值的加权平均值：达到或超过流量控制阈值的贡献值权重是较低压力贡献值的三倍。较快订阅反复产生的贡献值仍可能盖过少数落后订阅产生的压力。

<xref:Orleans.Configuration.StreamCacheEvictionOptions.DataMinTimeInCache> 和 <xref:Orleans.Configuration.StreamCacheEvictionOptions.DataMaxAgeInCache> 控制基于时间的缓存淘汰；它们并不保证每个订阅都处于缓存范围内。如果淘汰进度超过某个落后游标，投递将报告 `Item not found in cache`。

慢速消费监视器允许单个已观测到的落后游标施加缓存压力，而非将其压力与较快游标的压力求平均：

:::code source="snippets/streaming/EventHubCachePressure.cs" id="event_hub_slow_consumer_pressure":::

当分区缓存至少跨越 10,000 个 Event Hubs 序列号时，监视器开始计算游标压力。在本例中，当 Orleans 为某个订阅读取下一条缓存数据，且该订阅的游标比最新缓存位置落后超过缓存跨度的 70% 时，提供程序至少暂停新的 Event Hubs 读取 10 秒。根据实测滞后和处理时间调优这两个值。慢速消费策略有意限制分区接收速率，以保护观测到的最慢订阅：缓存未命中的可能性降低，但所有订阅的端到端滞后都可能增大，积压可能转移到 Event Hubs。确保 Event Hubs 的保留期限能够承受该积压，并且持续流入的事件速率不超过必须支持的最慢订阅的处理能力。

Orleans 在推进各订阅以处理缓存数据项时采样压力，因此应限制消费者执行轮次的工作量，使检测保持及时。首先缓解 CPU 饱和及热点 Grain 瓶颈。如果工作负载需要独立吞吐量或保留策略，使用不同的 Orleans 流提供程序及 Event Hubs 消费者组将其隔离，而不是通过一个分区缓存将其耦合在一起。

<a id="tune-the-pulling-pipeline"></a>

## 调优拉取流水线

持久化流提供程序通过各自的流配置器公开公共配置：

- <xref:Orleans.Configuration.StreamPullingAgentOptions.GetQueueMsgsTimerPeriod> 用于在轮询频率与延迟、服务调用次数之间权衡。
- <xref:Orleans.Configuration.StreamPullingAgentOptions.BatchContainerBatchSize> 控制每次投递时合并多少个队列批次。
- <xref:Orleans.Configuration.StreamPullingAgentOptions.MaxEventDeliveryTime> 限制调用已配置故障处理程序之前的投递尝试时长。
- <xref:Orleans.Configuration.SimpleQueueCacheOptions.CacheSize> 控制使用简单队列缓存的提供程序的数据项容量。

提供程序专用的控制项与通用控制项同样重要：<xref:Orleans.Configuration.AzureQueueOptions.QueueNames>、Event Hubs 分区及缓存压力设置、Redis `ReadCount` 及保留策略、NATS `BatchSize` 和 `PartitionCount`，以及 ADO.NET 的可见性、过期与死信设置。

每次仅调整一个瓶颈。更多队列可以提高并行度，但也会增加消息中间件成本、轮询负载、缓存内存用量和再均衡工作量。缩短轮询延迟可降低延迟，但会增加空读取次数。

<a id="tune-memory-stream-dequeue-batches"></a>

### 调优内存流出队批次

对于每个具名内存流提供程序，<xref:Orleans.Configuration.MemoryStreamCacheOptions.MaxAddCount> 限制单次出队操作请求的队列记录数。默认值为 `100`，且选项验证要求其大于零。使用 <xref:Orleans.Hosting.MemoryStreamConfiguratorExtensions.ConfigureCache*> 在提供程序的 `AddMemoryStreams` 回调中配置 Silo 上的此选项。对于基于配置的提供程序注册，在具名内存提供程序的配置节中设置 `MaxAddCount`。

每条记录包含一个已发布的事件批次。类似 `25` 的值会减少每个队列 Grain 响应中合并的记录数，但要达到相同吞吐量，就需要进行更多出队调用。较大的值将调用开销分摊到更多记录上，但可能增加每次调用的序列化工作量、响应大小及内存使用量。

此限制以记录数计量。总字节数取决于这些记录中的序列化有效载荷，包括各已发布批次中的所有事件。合理设置生产者的批次和有效载荷大小，使完整出队响应符合已配置的 Orleans 消息正文大小限制。队列 Grain 在响应序列化之前就已移除记录；因此，响应过大导致序列化失败时可能丢失这些记录。结合实测响应大小及队列滞后调优该数量。

<a id="observe-health"></a>

## 观测健康状况

导出 Orleans 指标计量器，并将其与消息中间件指标及应用程序事件 ID 关联。有用的 Orleans 度量工具包括：

| 信号 | 度量工具 |
|---|---|
| 活动拓扑 | `orleans-streams-pubsub-producers`、`orleans-streams-pubsub-consumers`、`orleans-streams-persistent-stream-pulling-agents` |
| 吞吐量 | `orleans-streams-persistent-stream-messages-read`、`orleans-streams-persistent-stream-messages-sent`、`orleans-streams-queue-messages-received` |
| 队列健康状况 | `orleans-streams-queue-read-failures`、`orleans-streams-queue-read-exceptions`、`orleans-streams-queue-oldest-message-enqueue-age` |
| 缓存健康状况 | `orleans-streams-queue-cache-size`、`orleans-streams-queue-cache-length`、`orleans-streams-queue-cache-pressure`、`orleans-streams-queue-cache-under-pressure` |
| 内存 | `orleans-streams-block-pool-total-memory`、`orleans-streams-block-pool-available-memory` |

对持续增大的最早消息存留时长、缓存压力、读取失败、重复消费者异常、死信增长，以及预期订阅数与活动订阅数不符的情况发出警报。仍需配置消息中间件侧的积压和保留期限告警，因为 Orleans 只能报告适配器所观测到的情况。

有关这些信号背后的组件，参见[Orleans 流处理实现](../implementation/streams-implementation/index.md)。
