---
title: "撰写自定义的持久性流队列调整器"
description: "实施并注册一个 Orleans 长流队列适配器,用于外部队列技术。"
ms.date: 08/18/2026
ms.topic: how-to
---

<a id="write-a-custom-persistent-stream-queue-adapter"></a>

# 撰写自定义的持久性流队列调整器

使用自定义队列适配器将 Orleans 持久性流运行时间与有其自身运输和交付语义的队列技术连接。 适配器翻译 Orleans 串流批次和外部队列之间。 Orleans 提供持续流源提供者、 拉动代理、订阅路由、队列平衡以及缓存管理。

当 Azure 队列存储或 Azure 事件枢纽已经提供了所需的运输行为时, [数据数据交换器](data-adapters.md) 将自定义其线条格式,同时保留内置的队列适配器。

注册一个 <xref:Orleans.Streams.IQueueAdapterFactory> ,其中含有 `AddPersistentStreams`; Orleans 创建和主机 <xref:Orleans.Providers.Streams.Common.PersistentStreamProvider>。

在应用适应器之前,请先理解 [持久性流拉动结构](../implementation/streams-implementation/index.md)。特别是,应决定适应器的交付、确认、订购、分割和回转语义。

<a id="implement-the-transport-boundary"></a>

## 执行运输边界

保持队列 SDK 和电路格式逻辑, 并保留在传输抽象后面。 运输必须:

- 将流体编号、事件有效载荷和请求上下文序列成可变信封;
- 在每个队列分区内指定单单调顺序序列号;
- 仅返回来自所请求分区的消息；
- 仅在 Orleans 调用完成方法后确认或删除信件;以及
- 表面队列失败, 而不是返回一个成功的空读取 。

:::code language="csharp" source="snippets/streaming/CustomQueueAdapter.cs" id="custom_queue_transport":::

当队列重发信件时序列号必须保持稳定。 示例指定了传输中的序列号, 因此它会拒绝在制作事件时使用呼叫器提供的序列符号 。

<a id="implement-the-adapter-and-receiver"></a>

## 实施适配器和接收器

<xref:Orleans.Streams.IQueueAdapter> handles writes and creates one receiver per queue partition. The stream-to-queue mapper used for writes must be the same mapper returned by the factory.

:::code language="csharp" source="snippets/streaming/CustomQueueAdapter.cs" id="custom_queue_adapter":::

<xref:Orleans.Streams.IQueueAdapterReceiver> reads queue messages and acknowledges them after every consumer has processed them. If the queue uses visibility leases, renew them while Orleans retains the message and make shutdown cancel outstanding reads.

:::code language="csharp" source="snippets/streaming/CustomQueueAdapter.cs" id="custom_queue_receiver":::

批量容器恢复了流特性、每件活动序列符号,并在 Orleans 交付批量时请求上下文。

:::code language="csharp" source="snippets/streaming/CustomQueueAdapter.cs" id="custom_queue_batch":::

<a id="implement-the-factory"></a>

## 工厂的安装

工厂组成了配对器,配有队列映射、缓存和故障处理。使用命名选项是因为一个程序可以注册多个名称不同的供应商。

:::code language="csharp" source="snippets/streaming/CustomQueueAdapter.cs" id="custom_queue_factory":::

`SimpleQueueAdapterCache` 适合不倒放的适配器,其队列仍然为耐久边界。可倒放的适配器通常需要缓存和序列执行,可以将光标定位在保留的历史信息上。

自定义集合缓存使用 <xref:Orleans.Providers.Streams.Common.ICacheDataAdapter.Compare*> 将光标定位在隐藏信件上。默认执行比对 `SequenceNumber` 和 `EventIndex`,保存现有提供者使用的数值顺序。当权威提供方位置编码在 <xref:Orleans.Providers.Streams.Common.CachedMessage.Segment>中时,它会覆盖;返回与 <xref:Orleans.Providers.Streams.Common.ICacheDataAdapter.GetSequenceToken*> 产生的符号相符的订单,因此缓存界限、块选择和缓存失密检测使用相同的位置合同。

`AddPersistentStreams` 将检查站留至适配器。不可倒转示例确认通过接收器已完成信件,因此没有独立的检查站。对于保留式运输,执行 <xref:Orleans.Streams.IStreamQueueCheckpointerFactory>,拥有接收器或缓存装载并更新每个分区的位置,并将它登记为 `ConfigureComponent`的指定组成部分。只有在所有消费者都超过相应的缓存信息之后,才能设置一个检查站。只有当重放位置被故意一次性时,才适合设置不操作检查站。

永久海关检查站后端,执行 <xref:Orleans.Streams.IStreamCheckpointStore> 传递给 <xref:Orleans.Streams.StreamQueueCheckpointer> 与 <xref:Orleans.Streams.StreamQueueCheckpointerOptions>。在处理分区之前装入检查器。每个仓库更新都收到预期后端版本并返回由此产生的 <xref:Orleans.Streams.StreamCheckpointStoreState>,因此冲突版本提供权威检查站和重试或调节版本。

<xref:Orleans.Streams.StreamQueueCheckpointer> limits writes to the configured persistence interval, coalesces pending updates to the latest checkpoint, and flushes the latest position on demand. Set <xref:Orleans.Streams.StreamQueueCheckpointerOptions.CheckpointComparer> when checkpoint values have an ordering contract; the checkpointer then keeps progress monotonic across local updates and concurrent store writers.

<a id="register-the-provider"></a>

## 注册供应商

登记依赖注射的运输客户,然后通过工厂 `Create` 方法将该设备传送到 `AddPersistentStreams`。 通过供应商配置配置配置配置配置队列数和缓存能力。

:::code language="csharp" source="snippets/streaming/CustomQueueAdapter.cs" id="custom_queue_silo_registration":::

直接生产或消费流的 Orleans 客户,登记同一供应商名称和兼容绘图:

:::code language="csharp" source="snippets/streaming/CustomQueueAdapter.cs" id="custom_queue_client_registration":::

保持提供者名称和分区数的稳定。 更改后两者中任何一个都可以将现有串流映射为不同的队列和先前排队的信件。 配置耐用 `PubSubStore` grain 存储, 用于制作中的明确订阅; `PubSubStore` 保存订阅记录而不保留队列的耐久性 。

<a id="recover-persisted-token-positions-during-an-upgrade"></a>

## 在升级期间回收已存在的待发贴记职位

先前继承的 `CreateSequenceTokenForEvent` 实施过程返回了精确的 <xref:Orleans.Providers.Streams.Common.EventSequenceToken> 或 <xref:Orleans.Providers.Streams.Common.EventSequenceTokenV2>,包括批量凭证有一个定制子类型。当前工厂在改变事件指数的同时保存了具体的子类型及其元数据。

251.Orleans 根据供应商的象征性合同收回这些保存的货位:

| 托肯合同 | 恢复期间支持的常住职位 |
| --- | --- |
| 通用 V1/V2 和自定义子类 明确选择通用兼容域 | V1和V2的精确位置与当前象征物相比,按序列号和事件指数分列,并符合全家庭的平等性和杂乱无章。 |
| 事件 Hubs V1/V2 和自定义子分类 明确选择事件 Hubs兼容域 | 先前继承的工厂的精确V1职位在事件枢纽回收比较中实现正常化。序列编号和事件指数确定位置;当前交付的标牌保留了活动枢纽的抵消和自定义元数据。 |
| Kinesis 键基 | 永久的 Kinesis 符号保留数字碎片偏移作为权威位置,然后是接收器重开时的事件索引。 |
| Redis | 永久 Redis 质证保留条目编号、每毫秒序列号和事件索引。 |

衍生符号默认是孤立的, 它保留了在此兼容勾存在之前所编集的适应器的身份合同。 一个使用完整的通用数字合同的自定义符号将 `SequenceTokenCompatibilityDomain` 替换回 `typeof(EventSequenceToken)`。 相关符号版本选择相同的稳定域类型。 一个添加位置身份的自定义符号保留了一个不同的域, 并持续地执行该域的平等、 订购和散列 。

事件枢纽、 Kinesis 和 Redis 都保留了它们各自与供应商具体相关的公共平等合同。 事件枢纽回收使用一个仅序列的标记,该标记只包含一个空位,用于工厂遗漏部分的遗留位置; 抵消符来自当前提供者的数据。 在重放持续位置时保持流身份和分区映射稳定 。

<a id="validate-failure-behavior"></a>

## 校验失败行为

测试适配器与真实队列服务相比,包括:

1. 含有多事件类型和请求-EXT值的批次;
1. 空读、取消、瞬时错误、抽动和关闭;
1. 生产商、接收器和 silo 在确认之前和确认之后均未能履行;
1. 在成员变更期间,队列所有权在 silos 之间移动;
1. 重复交付和消费者充分能力;
1. 跨越重新启动和升级的稳定的流到部间绘图;
1. 超出缓冲能力以核查后压和排队保留的持续负荷;以及
1. 顺序和顺序平等,顺序和散列 在两个比较方向。

使用分隔、接收和识别延迟时间、再交付计数、抽打、拉动剂错误以及消费者交付失败等手段监测队列深度和最古老信息年龄。 保留前的警告或可见度限制可能导致数据丢失或再次交付风暴。
