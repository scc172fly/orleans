---
title: 选择持久化流订阅的起始位置
description: 使 Orleans 持久化流订阅从新消息开始，或重放本地队列缓存中保留的消息。
ms.date: 09/03/2026
ms.topic: how-to
---

<a id="choose-a-persistent-stream-subscription-start-position"></a>

# 选择持久化流订阅的起始位置

支持回溯的持久化流订阅可以从新发布的消息开始，也可以重放其拉取代理已保留的消息。在应用程序创建订阅时选择起始位置，或为未指定位置的订阅配置提供程序级默认值。投递开始后，订阅位置由序列令牌所记录的进度决定。

有关更完整的订阅模型，请参阅 [Orleans 流 API](streams-programming-apis.md)。有关提供程序的持久性和回溯能力，请参阅 [Orleans 流提供程序](stream-providers.md)。

<a id="choose-a-position-for-one-subscription"></a>

## 为单个订阅选择起始位置

将 <xref:Orleans.Streams.StreamSubscriptionStartPosition> 传递给 <xref:Orleans.Streams.AsyncObservableExtensions.SubscribeAsync*>：

:::code language="csharp" source="snippets/subscription-start-positions/SubscriptionStartPositions.cs" id="subscribe_earliest_available":::

批处理观察者重载接受相同的起始位置：

:::code language="csharp" source="snippets/subscription-start-positions/SubscriptionStartPositions.cs" id="subscribe_batch_earliest_available":::

各个值具有以下语义：

| 位置 | 初始投递 |
|---|---|
| <xref:Orleans.Streams.StreamSubscriptionStartPosition.Latest> | 订阅建立后发布的消息。跳过队列缓存中已保留的消息。此值保留未提供令牌的 `SubscribeAsync` 调用的默认行为。 |
| <xref:Orleans.Streams.StreamSubscriptionStartPosition.EarliestAvailable> | 从拉取代理本地队列缓存中当前保留的最早匹配消息开始，并包含该消息。如果缓存中没有匹配的消息，则从该流未来到达的第一条消息开始投递。 |

起始位置仅在创建订阅时适用。恢复现有订阅句柄，以便从其已建立的进度继续；恢复 API 使用序列令牌，而不是新的起始位置。

<a id="configure-the-provider-default"></a>

## 配置提供程序的默认值

在 Silo 端的持久化流提供程序上配置 <xref:Orleans.Configuration.StreamPullingAgentOptions.InitialSubscriptionStartPosition>：

:::code language="csharp" source="snippets/subscription-start-positions/SubscriptionStartPositions.cs" id="configure_default_start_position":::

此设置控制既未指定具体序列令牌，也未指定显式起始位置的初始订阅。适用于未提供令牌的显式订阅，以及尚无投递进度的隐式订阅初次附接。默认值为 <xref:Orleans.Streams.StreamSubscriptionStartPosition.Latest>。

Orleans 按以下顺序选择初始位置：

1. 调用方提供的具体 <xref:Orleans.Streams.StreamSequenceToken>。
1. 显式提供的 <xref:Orleans.Streams.StreamSubscriptionStartPosition>，传递给 `SubscribeAsync`。
1. <xref:Orleans.Configuration.StreamPullingAgentOptions.InitialSubscriptionStartPosition>。
1. <xref:Orleans.Streams.StreamSubscriptionStartPosition.Latest>。

因此无论选择哪种方向，显式选项都会覆盖提供程序的默认值。显式指定 `Latest` 时，即使提供程序默认值是 `EarliestAvailable`，也会跳过已保留消息；显式指定 `EarliestAvailable` 时，即使提供程序默认值是 `Latest`，也会重放本地缓存。在所有能够承载该命名提供程序拉取代理的 Silo 上，应保持此选项的配置一致。

<a id="understand-the-available-replay-window"></a>

## 了解可用的重放窗口

`EarliestAvailable` 在一个拉取代理的本地队列缓存中查找目标 <xref:Orleans.Runtime.StreamId>。缓存当前的内容决定可用的重放窗口。缓存逐出、内存压力、队列分配、Silo 重启以及拉取代理开始读取后的经过时间，都可能使最早可用位置向前移动。

对于 Azure Event Hubs，`EarliestAvailable` 不会改变分区接收器和其检查点的当前位置。它重放 Orleans Event Hubs 拉取代理已经读取且仍保留在 Silo 端缓存中的匹配消息。因此，本地缓存窗口是此 API 的重放窗口，而 Event Hubs 的数据保留仍决定上游恢复窗口。

<xref:Orleans.Configuration.StreamCacheEvictionOptions.DataMinTimeInCache> 和 <xref:Orleans.Configuration.StreamCacheEvictionOptions.DataMaxAgeInCache> 控制基于时间的缓存逐出，缓存压力也可能使最早保留位置向前移动。根据上游恢复要求设置 Event Hubs 的保留期限，并根据新订阅所需的重放时间范围设置 Orleans 缓存容量。有关缓存逐出和压力处理行为，请参阅[保护处理缓慢的 Event Hubs 消费者](streaming-operations.md#protect-a-slow-event-hubs-consumer)。

<a id="handle-queue-cache-support"></a>

## 处理队列缓存的支持情况

内置的池化、简单、内存、生成器和 Event Hubs 队列缓存均支持 `EarliestAvailable`。自定义持久化流缓存可以实现 <xref:Orleans.Streams.IQueueCache.TryGetCacheCursorAtPosition*> 来支持该选项，并返回成功的游标结果，游标定位于该流保留的最早消息，且包含该消息。

队列缓存接口的默认行为是将 `Latest` 映射到通过 <xref:Orleans.Streams.IQueueCache.TryGetCacheCursor*> 进行的无令牌获取，并返回 <xref:Orleans.Streams.QueueCacheCursorResultKind.NotSupported> 以表示不支持 `EarliestAvailable`。拉取代理在订阅边界将不支持的位置转换为 <xref:System.NotSupportedException>。请求不支持位置的显式订阅会收到错误并进入故障状态。当 `EarliestAvailable` 是隐式订阅的提供程序默认值时，Orleans 会向消费者报告错误，并使隐式订阅在其当前位置保持活动。

自定义 <xref:Orleans.Streams.IAsyncObservable`1> 实现具有等效的扩展方法重载兼容性：`Latest` 使用其无令牌的订阅路径，而 `EarliestAvailable` 报告 <xref:System.NotSupportedException>，直至可观察对象实现 Orleans 起始位置订阅。

有关投递保证和序列令牌恢复，请继续阅读[流投递、顺序、重放和恢复](delivery-semantics.md)。
