---
title: 编写自定义持久化流队列适配器
description: 为外部队列技术实现并注册 Orleans 持久化流队列适配器。
ms.date: 08/18/2026
ms.topic: how-to
---

<a id="write-a-custom-persistent-stream-queue-adapter"></a>

# 编写自定义持久化流队列适配器

使用自定义队列适配器，将 Orleans 持久化流运行时连接到具有自身传输和投递语义的队列技术。适配器负责在 Orleans 流批次与外部队列之间转换。Orleans 提供持久化流提供程序、拉取代理、订阅路由、队列均衡以及缓存管理。

如果 Azure Queue Storage 或 Azure Event Hubs 已提供所需的传输行为，可以通过[数据适配器](data-adapters.md) 自定义传输格式，同时继续使用内置队列适配器。

注册 <xref:Orleans.Streams.IQueueAdapterFactory> 时使用 `AddPersistentStreams`；Orleans 会创建并托管 <xref:Orleans.Providers.Streams.Common.PersistentStreamProvider>。

实现适配器之前，请先了解[持久化流拉取架构](../implementation/streams-implementation/index.md)。尤其要确定适配器的投递、确认、排序、分区及回溯语义。

<a id="implement-the-transport-boundary"></a>

## 实现传输边界

将队列 SDK 和传输格式处理逻辑封装在传输抽象之后。传输层必须满足以下要求：

- 将流 ID、事件有效载荷和请求上下文序列化为可演进的消息信封；
- 在每个队列分区内分配单调递增的序列号；
- 仅返回所请求分区中的消息；
- 仅在 Orleans 调用完成方法后确认或删除消息；以及
- 将队列故障报告出来，而不是返回成功但结果为空的读取操作。

:::code language="csharp" source="snippets/streaming/CustomQueueAdapter.cs" id="custom_queue_transport":::

队列重新投递消息时，序列号必须保持不变。示例由传输层分配序列号，因此在生产事件时会拒绝调用方提供的序列令牌。

<a id="implement-the-adapter-and-receiver"></a>

## 实现适配器和接收器

<xref:Orleans.Streams.IQueueAdapter> 处理写入操作，并为每个队列分区创建一个接收器。写入时使用的流到队列的映射器必须与工厂返回的映射器相同。

:::code language="csharp" source="snippets/streaming/CustomQueueAdapter.cs" id="custom_queue_adapter":::

<xref:Orleans.Streams.IQueueAdapterReceiver> 读取队列消息，并在所有消费者均处理完成后确认消息。如果队列使用可见性租约，则应在 Orleans 保留消息期间续订租约，并在关闭时取消未完成的读取。

:::code language="csharp" source="snippets/streaming/CustomQueueAdapter.cs" id="custom_queue_receiver":::

Orleans 投递批次时，批次容器恢复流标识、每个事件的序列令牌及请求上下文。

:::code language="csharp" source="snippets/streaming/CustomQueueAdapter.cs" id="custom_queue_batch":::

<a id="implement-the-factory"></a>

## 实现工厂

工厂将适配器与队列映射、缓存和故障处理组合起来。一个进程可以注册多个名称不同的提供程序，因此请使用具名选项。

:::code language="csharp" source="snippets/streaming/CustomQueueAdapter.cs" id="custom_queue_factory":::

`SimpleQueueAdapterCache` 适用于不支持回溯、且队列仍是持久化边界的适配器。支持回溯的适配器通常需要能够将游标定位到保留的历史消息的缓存和序列令牌实现。

自定义池化缓存使用 <xref:Orleans.Providers.Streams.Common.ICacheDataAdapter.Compare*> 将游标定位到缓存消息。默认实现比较 `SequenceNumber` 和 `EventIndex`，保持现有提供程序使用的数值排序。如果权威提供程序位置编码在 <xref:Orleans.Providers.Streams.Common.CachedMessage.Segment> 中，请重写该方法；返回的顺序必须与 <xref:Orleans.Providers.Streams.Common.ICacheDataAdapter.GetSequenceToken*> 生成的令牌一致，从而使缓存边界、块选择和缓存未命中检测遵循同一位置契约。

`AddPersistentStreams` 将检查点管理交给适配器。不支持回溯的示例通过接收器确认已处理完毕的消息，因此没有独立检查点。对于保留日志的传输方式，实现 <xref:Orleans.Streams.IStreamQueueCheckpointerFactory>，使接收器或缓存加载并更新各分区的位置，并通过 `ConfigureComponent` 将其注册为具名组件。仅在所有消费者均已越过相应缓存消息后持久化检查点。只有明确允许丢弃重放位置时，才适合使用空操作检查点管理器。

若要实现持久化的自定义检查点后端，实现 <xref:Orleans.Streams.IStreamCheckpointStore>，将其传递给 <xref:Orleans.Streams.StreamQueueCheckpointer>，并使用 <xref:Orleans.Streams.StreamQueueCheckpointerOptions> 进行配置。在处理分区前加载检查点管理器。每次存储更新都会接收预期的后端版本，并返回更新后的 <xref:Orleans.Streams.StreamCheckpointStoreState>；因此发生版本冲突时，可以获得权威检查点及版本，以便重试或协调。

<xref:Orleans.Streams.StreamQueueCheckpointer> 按配置的持久化间隔限制写入频率，将待处理的更新合并为最新检查点，并按需将最新位置刷新到存储中。如果检查点值具有排序契约，请设置 <xref:Orleans.Streams.StreamQueueCheckpointerOptions.CheckpointComparer>；检查点管理器随后会在本地更新和并发存储写入者之间保持进度单调递增。

<a id="register-the-provider"></a>

## 注册提供程序

在依赖注入容器中注册传输客户端，然后将工厂的 `Create` 方法传给 `AddPersistentStreams`。通过提供程序配置器设置队列数量和缓存容量。

:::code language="csharp" source="snippets/streaming/CustomQueueAdapter.cs" id="custom_queue_silo_registration":::

对于直接生产或消费流的 Orleans 客户端，注册相同的提供程序名称及兼容的映射：

:::code language="csharp" source="snippets/streaming/CustomQueueAdapter.cs" id="custom_queue_client_registration":::

保持提供程序名称和分区数量稳定。任意一项发生变化都可能将现有流映射到其他队列，使此前已入队的消息滞留。在生产环境中，为显式订阅配置持久化的 `PubSubStore` Grain 存储；`PubSubStore` 独立于队列持久性保存订阅记录。

<a id="recover-persisted-token-positions-during-an-upgrade"></a>

## 升级期间恢复持久化的令牌位置

早期继承的 `CreateSequenceTokenForEvent` 实现返回类型恰好为 <xref:Orleans.Providers.Streams.Common.EventSequenceToken> 或 <xref:Orleans.Providers.Streams.Common.EventSequenceTokenV2> 的实例，即使批次令牌属于自定义子类型也是如此。当前工厂在更改事件索引时保留具体子类型及其元数据。

Orleans 根据提供程序的令牌契约恢复这些已保存的位置：

| 令牌契约 | 恢复期间支持的持久化位置 |
| --- | --- |
| 通用 V1/V2 及显式选择通用兼容性域的自定义子类 | 类型恰好为 V1、V2 的位置与当前令牌按序列号和事件索引进行比较；整个令牌族具有一致的相等性和哈希值。 |
| Event Hubs V1/V2 及显式选择 Event Hubs 兼容性域的自定义子类 | 早期继承工厂生成的、类型恰好为 V1 的位置在 Event Hubs 恢复比较中进行规范化。序列号和事件索引标识位置；当前投递的令牌保留 Event Hubs 偏移量和自定义元数据。 |
| Kinesis | 接收器重启后，持久化的 Kinesis 令牌仍将数值型分片偏移量作为权威位置，随后比较事件索引。 |
| Redis | 持久化的 Redis 令牌保留条目 ID、同一毫秒内的序列号以及事件索引。 |

派生令牌默认相互隔离，这样可以保持此兼容性扩展点出现前编译的适配器的标识契约。使用完整通用数值契约的自定义令牌应重写 `SequenceTokenCompatibilityDomain` 以返回 `typeof(EventSequenceToken)`。相关令牌版本选择相同且稳定的域类型。增加位置标识信息的自定义令牌应保留独立的域，并为该域一致地实现相等性、排序及哈希计算。

Event Hubs、Kinesis 和 Redis 各自保留提供程序特定的公开相等性契约。Event Hubs 恢复时，对早期工厂遗漏偏移量的旧版位置使用偏移量为空、仅含序列号的令牌；包含偏移量的令牌来自当前提供程序数据。重放持久化位置时，请保持流标识及分区映射稳定。

<a id="validate-failure-behavior"></a>

## 验证故障行为

使用真实队列服务测试适配器，测试范围包括：

1. 包含多种事件类型及请求上下文值的批次；
1. 空读取、取消、暂时性错误、限流和关闭；
1. 确认前后发生的生产者、接收器及 Silo 故障；
1. 成员资格变化期间队列所有权在 Silo 之间迁移；
1. 重复投递及消费者幂等性；
1. 跨重启及升级的流到分区映射稳定性；
1. 持续超过缓存容量的负载，以验证背压及队列保留行为；以及
1. 两个比较方向上的序列令牌相等性、排序及哈希计算。

按分区监控队列深度、最早消息存留时长、接收和确认延迟、重新投递次数、限流、拉取代理错误以及消费者投递失败。在保留期限或可见性限制可能造成数据丢失或重新投递风暴之前发出警报。
