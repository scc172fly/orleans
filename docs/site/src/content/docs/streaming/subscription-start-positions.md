---
title: "选择一个持续流订阅启动位置"
description: "启动 Orleans 持久性流订阅,使用新信件或重放本地队列缓存中保留的信件。"
ms.date: 09/03/2026
ms.topic: how-to
---

<a id="choose-a-persistent-stream-subscription-start-position"></a>

# 选择一个持续流订阅启动位置

可倒放的持续性流订阅可以从新发布的信件开始,或者重放已经由拖动代理保存的信件。选择应用程序行为创建订阅时的起始位置,或者配置一个全供应商范围的默认订阅,而该订阅会省略位置。交付开始后,序列到脚进度将决定订阅的位置。

更广泛的订阅模式见 [Orleans 流出的API](streams-programming-apis.md)。 [Orleans 流流提供者](stream-providers.md)中描述了供应商的持久性和可倒转性。

<a id="choose-a-position-for-one-subscription"></a>

## 选择一个订阅位置

将 <xref:Orleans.Streams.StreamSubscriptionStartPosition> 传给 <xref:Orleans.Streams.AsyncObservableExtensions.SubscribeAsync*>:

:::code language="csharp" source="snippets/subscription-start-positions/SubscriptionStartPositions.cs" id="subscribe_earliest_available":::

批量观察器超载接受同样的位置:

:::code language="csharp" source="snippets/subscription-start-positions/SubscriptionStartPositions.cs" id="subscribe_batch_earliest_available":::

价值有这些语义学:

| 职位位置位置 | 初次交付 |
|---|---|
| <xref:Orleans.Streams.StreamSubscriptionStartPosition.Latest> | 订阅后发布的信件。 已经保存在队列缓存中的信件会被跳过。 此值保留无线 `SubscribeAsync` 呼叫的默认行为 。 |
| <xref:Orleans.Streams.StreamSubscriptionStartPosition.EarliestAvailable> | 拖拉机本地队列缓存中目前保存的最早匹配信件, 包括发送。 当缓存没有匹配的信件时, 传送从流中的第一个未来消息开始 。 |

当创建订阅时, 位置将适用。 恢复现有的订阅程序以继续其既定进度; 恢复 API 使用序列符号而不是新的启动位置 。

<a id="configure-the-provider-default"></a>

## 配置提供方默认默认默认配置

配置配置 <xref:Orleans.Configuration.StreamPullingAgentOptions.InitialSubscriptionStartPosition> 排在《公约》 silo- 持久性流提供者:

:::code language="csharp" source="snippets/subscription-start-positions/SubscriptionStartPositions.cs" id="configure_default_start_position":::

此设置控制初始订阅, 既省略具体序列符号, 也省略明确的起始位置。 它适用于无交付进度的无价明确订阅和初始隐含订阅附件。 <xref:Orleans.Streams.StreamSubscriptionStartPosition.Latest> 是默认的 。

依此顺序选择初始位置Orleans :

1. 具体 <xref:Orleans.Streams.StreamSequenceToken> 电话人提供
1. 明示 <xref:Orleans.Streams.StreamSubscriptionStartPosition> 供应的 `SubscribeAsync`.
1. <xref:Orleans.Configuration.StreamPullingAgentOptions.InitialSubscriptionStartPosition>.
1. <xref:Orleans.Streams.StreamSubscriptionStartPosition.Latest>.

因此,明确选择可在任一方向上优先于提供者违约。 `Latest` 当提供方默认为默认时, 跳过保留的信件 `EarliestAvailable`明确 `EarliestAvailable` 当服务提供方默认为本地缓存时重播放 `Latest`。在每一个 silo 有资格为指定供应商主办一个拖拉机。

<a id="understand-the-available-replay-window"></a>

## 理解可用的重放窗口

`EarliestAvailable` 搜索一个拉动代理器的本地队列缓存以查找目标 <xref:Orleans.Runtime.StreamId>。缓冲当前内容定义了可用的重放窗口。缓存驱离、内存压力、队列任务、 silo 重新启动,以及拖动代理器开始读取后的时间,都可以将其最早的位置向前移动。

将 Azure 事件枢纽改为 `EarliestAvailable` ,使分区接收器及其检查站保持其当前位置。它重放与 Orleans 事件枢纽拉动代理已经读取并仍然保留在 silo- 侧缓存中的信件匹配的信息。因此,本地缓存窗口是此 API的复放窗口,而“事件枢纽”保留仍然是上游恢复窗口。

<xref:Orleans.Configuration.StreamCacheEvictionOptions.DataMinTimeInCache> and <xref:Orleans.Configuration.StreamCacheEvictionOptions.DataMaxAgeInCache> control time-based eviction, while cache pressure can advance the earliest retained position. Size Event Hubs retention for upstream recovery and size the Orleans cache for the replay interval required by new subscriptions. See [Protect a slow Event Hubs consumer](streaming-operations.md#protect-a-slow-event-hubs-consumer) for cache eviction and pressure behavior.

<a id="handle-queue-cache-support"></a>

## 处理队列缓存支持

内置集、简单、内存、生成器和事件枢纽队列缓存支持 `EarliestAvailable`。自定义的持久性流缓存通过实施 <xref:Orleans.Streams.IQueueCache.TryGetCacheCursorAtPosition*> 参与,并返回光标成功的结果,该结果被包含在流中最古老保留的信息处。

默认的队列缓存界面行为映射 `Latest` 以 <xref:Orleans.Streams.IQueueCache.TryGetCacheCursor*> 返回和返回 <xref:Orleans.Streams.QueueCacheCursorResultKind.NotSupported> 用于 `EarliestAvailable`。拖动代理器将一个无支持的位置转换成 <xref:System.NotSupportedException> 订阅边界。要求不支持位置的明确的订阅收到错误,但被错误。 `EarliestAvailable` 是默认订阅的提供者, Orleans 将错误报告给消费者,并让暗含订阅按其目前的位置直播。

自定义 <xref:Orleans.Streams.IAsyncObservable`1> 实施程序具有同等的扩展加载兼容性: `Latest` 使用其无价订阅路径, `EarliestAvailable` 报告 <xref:System.NotSupportedException> ,直到可观测到执行 Orleans 启动点订阅。

对于交货担保和序列回收,继续 [流流输送、订购、重播和回收](delivery-semantics.md)。
