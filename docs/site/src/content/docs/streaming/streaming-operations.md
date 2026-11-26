---
title: "运行和调制 Orleans 流"
description: "施压、调控耐用供应商,并观察 Orleans 流体健康。"
ms.date: 09/10/2026
ms.topic: concept-article
---

<a id="operate-and-tune-orleans-streams"></a>

# 运行和调制 Orleans 流

由时间差、吞吐量、故障和记忆压力等衡量的时滞、输液量、失败和记忆压力来计算。 供应商违约是起点,而不是普遍生产设置。

<a id="backpressure"></a>

## 后压

持久性提供者将事件拖到 silo- 端缓存并交付订阅。 消费者在接受项目责任之前不完成 <xref:Orleans.Streams.IAsyncObserver`1.OnNextAsync*> ,从而施加后压。 因此,慢消费者可以增加保留队列数据、缓存压力和端至端滞后。

保持消费者旋转框框 :

- 坚持要求保持状态,并迅速完成。
- 将独立、长期的工作置于明确模型的搭接后。
- 避免同步阻塞和无限制平行工作。
- 选择足够多的提供者队列或分区, 并分配流键来缩放。 一个针对一个状态 grain 的热流仍然受 grain处理率的限制 。

消费者方面的后压并不意味着生产者完成生产。 `OnNextAsync` 生产者报告供应商接受,而不是下游消费者赶上。

<a id="protect-a-slow-event-hubs-consumer"></a>

### 保护慢事件枢纽消费者

事件枢纽提供商为每次订阅维持独立的缓存光标,这样快速订阅就可以继续,而另一个用户则落后。 但是,所有事件枢纽分区的光标共享相同的 silo- 侧边快取。 默认情况下, 服务商使用这些光标压力贡献的加权平均值: 流量控制阈值或以上的捐款获得比低压捐款重三倍的分量。 较快的订阅者重复缴纳的会费仍然可以超过少数滞后的订阅数量。

<xref:Orleans.Configuration.StreamCacheEvictionOptions.DataMinTimeInCache> and <xref:Orleans.Configuration.StreamCacheEvictionOptions.DataMaxAgeInCache> control time-based cache eviction; they don't guarantee that every subscription remains within the cache. If eviction advances past a lagging cursor, delivery reports `Item not found in cache`.

慢耗监测器让一个观察到的滞后光标应用缓存压力,而不是用更快的光标平均地承受这种压力:

:::code source="snippets/streaming/EventHubCachePressure.cs" id="event_hub_slow_consumer_pressure":::

显示器在分区缓存至少覆盖10,000个事件枢纽序列编号后开始计算光标压力。 例如,当 Orleans 读取下一个订阅的缓存项目时, 其光标超过缓存位置70%的缓存点位于最新缓存位置之后, 提供者会停止新的“ 事件枢纽” 至少10秒。 以测量的滞后和处理时间来调试数值。 缓慢消费的政策故意限制分割摄取以保护最慢的被观察订阅: 缓存误差的可能性较小, 但每次订阅的端到端滞后可能会增加, 积压可以移到“ 事件枢纽” 中。 确保“ 事件枢纽” 保留能够吸收积压和持续不增不超出最慢的所需订阅能力 。

压力通过 Orleans 缓存项目作为预购用户样本进行抽样, 以便让消费者转圈以保持检测时速。 首先减少 CPU 饱和性和热 -grain 瓶颈。 如果工作量需要独立的吞吐或保留政策, 请使用单独的 Orleans 流提供者和事件枢纽消费群体来孤立它们, 而不是通过一个分隔缓存将它们连接在一起 。

<a id="tune-the-pulling-pipeline"></a>

## 调出管道管道

持续提供商通过流配置程序披露共同配置:

- <xref:Orleans.Configuration.StreamPullingAgentOptions.GetQueueMsgsTimerPeriod> trades polling frequency against latency and service calls.
- <xref:Orleans.Configuration.StreamPullingAgentOptions.BatchContainerBatchSize> controls how many queue batches are grouped for delivery.
- <xref:Orleans.Configuration.StreamPullingAgentOptions.MaxEventDeliveryTime> bounds delivery attempts before the configured failure handler is involved.
- <xref:Orleans.Configuration.SimpleQueueCacheOptions.CacheSize> controls item capacity for providers using the simple queue cache.

具体供应商控制措施与共同控制一样重要: <xref:Orleans.Configuration.AzureQueueOptions.QueueNames>、事件枢纽分割和缓冲压力设置、 Redis `ReadCount` 和保留、NATS `BatchSize` 和 `PartitionCount`,以及 ADO.NET 可见度、过期和空白字母设置。

一次换一个瓶颈。 更多的排队可以增加平行关系,但也能够增加中介成本、投票负荷、缓存和重新平衡工作。 减少选举延迟既能降低延缓率又能增加空读数。

<a id="tune-memory-stream-dequeue-batches"></a>

### Tunne 内存流解结队批量

每个命名的记忆流提供者, <xref:Orleans.Configuration.MemoryStreamCacheOptions.MaxAddCount> 框中,在单个取消后操作中要求的队列记录数的内框。默认值是 `100`,并且选项验证需要大于零值的数值。将其配置在 silo 使用 <xref:Orleans.Hosting.MemoryStreamConfiguratorExtensions.ConfigureCache*> 提供商的 `AddMemoryStreams` 回回调。 用于基于配置的供应商注册,设置 `MaxAddCount` 在命名的内存提供者配置部分。

每个记录包含一个已公布的系列事件。 诸如 `25` 这样的数值减少了每个队列-grain 响应中合并的记录数量,而成本是更多的解压要求相同的传输量。 更大的价值在更多记录之间摊合调用电话管理费,并可以增加序列化工作、响应大小和每通电话记忆的使用。

集字节大小取决于这些记录中的序列有效载荷,包括每一批已出版的物品中的所有事件。 Orleans 信件- body 限制。 队列 grain 在回复被序列化之前删除记录;因此,如果序列化失败,超大响应可能会丢失这些记录。 计数与测量的响应大小和队列滞后同时进行。

<a id="observe-health"></a>

## 健康观察

出口出口出口 Orleans 并且与经纪人的度量和应用程序事件ID相连接。 Orleans 包括:

| 信号信号 | 文书文书文书文书 |
|---|---|
| 活性地表 | `orleans-streams-pubsub-producers`, `orleans-streams-pubsub-consumers`, `orleans-streams-persistent-stream-pulling-agents` |
| 投入 | `orleans-streams-persistent-stream-messages-read`, `orleans-streams-persistent-stream-messages-sent`, `orleans-streams-queue-messages-received` |
| 保健队 | `orleans-streams-queue-read-failures`, `orleans-streams-queue-read-exceptions`, `orleans-streams-queue-oldest-message-enqueue-age` |
| 缓缓健康 | `orleans-streams-queue-cache-size`, `orleans-streams-queue-cache-length`, `orleans-streams-queue-cache-pressure`, `orleans-streams-queue-cache-under-pressure` |
| 内存 | `orleans-streams-block-pool-total-memory`, `orleans-streams-block-pool-available-memory` |

有关持续最老消息时代、缓冲压力、阅读失败、消费者的例外、死字母增长以及预期订阅和主动订阅数量不匹配的警报依然必要。 中间商积压和保留提醒仍然有必要,因为 Orleans 只能报告适应者所观察到的东西。

这些信号背后的部件见 [Orleans 流执行](../implementation/streams-implementation/index.md)。
