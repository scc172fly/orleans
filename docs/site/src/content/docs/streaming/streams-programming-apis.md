---
title: Orleans 流处理 API
description: 在 Orleans 中使用流标识、生产者、消费者以及显式或隐式订阅。
ms.date: 08/24/2026
ms.topic: concept-article
---

<a id="orleans-streaming-apis"></a>

# Orleans 流处理 API

<a id="stream-identity"></a>

## 流标识

<a id="async-stream"></a>

选择一个 Orleans 流需要三个应用层要素：

- **提供程序名称**选择已配置的 <xref:Orleans.Streams.IStreamProvider>。
- **流命名空间**对相关流进行分组，并参与隐式订阅匹配。
- **流键**标识命名空间中的一个流，可以是字符串、GUID 或整数。

命名空间和键共同组成 <xref:Orleans.Runtime.StreamId>。在共享代码中构造流标识：

:::code language="csharp" source="snippets/streaming/BasicStreaming.cs" id="stream_identity":::

<xref:Orleans.Streams.IStreamProvider.GetStream*> 返回强类型的 <xref:Orleans.Streams.IAsyncStream`1>。获取提供程序和流句柄是在本地执行的，不会创建消息中间件实体。生产者和消费者必须就 `T` 达成一致；有效载荷遵循 Orleans 序列化规则。

<a id="producers"></a>

## 生产者

<a id="producing-and-consuming"></a>

任何 Grain 或已配置的 Orleans 客户端都可以发布消息。一个流可以有多个生产者：

:::code language="csharp" source="snippets/streaming/BasicStreaming.cs" id="stream_producer":::

当发布顺序很重要时，应等待每次 <xref:Orleans.Streams.IAsyncObserver`1.OnNextAsync*> 调用完成。返回的任务表示提供程序已接受数据，而非端到端的消费已完成。提供程序故障以及结果不确定的超时需要应用程序自行决定重试和去重策略。

<a id="consumers"></a>

## 消费者

<a id="multiplicity"></a>

消费者附加 <xref:Orleans.Streams.IAsyncObserver`1> 实现或回调委托。`OnNextAsync` 接收数据项，并在提供程序提供令牌时接收 <xref:Orleans.Streams.StreamSequenceToken>。

只有在应用程序接管该数据项的处理责任后，才完成消费者任务。持久化流提供程序根据任务完成情况推进投递，或在故障后重试。避免阻塞线程；消费者的异步工作自然会向该订阅施加背压。

流采用多播模式。每个订阅都会接收每个数据项，单个 Grain 可以为同一个流创建多个显式订阅。每个订阅具有自己的 <xref:Orleans.Streams.StreamSubscriptionHandle`1>。

<a id="explicit-and-implicit-subscriptions"></a>

## 显式订阅和隐式订阅

提供程序的 <xref:Orleans.Streams.StreamPubSubType> 控制可用的订阅模式：

| 值 | 适用场景 | 权衡 |
|---|---|---|
| `ExplicitGrainBasedAndImplicit`（默认） | 应用程序同时使用两种模式，或预计订阅需求将不断演进。 | 灵活性最高。生产者注册会检查基于 Grain 的会合点和隐式 Grain 元数据；显式订阅部分需要 [`PubSubStore`](pubsub-storage.md)。 |
| `ExplicitGrainBasedOnly` | 所有消费者都使用运行时创建的订阅，包括客户端消费者。 | 专注于发现显式订阅。订阅及生产者的变化使用会合 Grain 和 `PubSubStore`，由应用程序管理订阅句柄及恢复。 |
| `ImplicitOnly` | 每个消费者都是通过 <xref:Orleans.ImplicitStreamSubscriptionAttribute> 声明的 Grain。 | 使用集群 Grain 元数据作为订阅目录，无需调用会合 Grain，也无需执行 `PubSubStore` 操作，因此发布/订阅控制平面开销最低，适合元数据定义的 Grain 订阅能够满足需求的场景。运行时创建的、可单独移除的订阅及客户端订阅使用支持显式订阅的模式。 |

此模式决定订阅的发现、协调和存储工作。事件传输和投递遵循所选择的流提供程序的语义。专用模式可以降低控制平面开销；根据所需订阅语义选择模式，并在应用程序的实际工作负载下测量其效果。

<a id="change-the-configured-mode"></a>

### 更改已配置的模式

通过更新 <xref:Orleans.Hosting.PersistentStreamConfiguratorExtensions.ConfigureStreamPubSub*> 并重启使用该具名提供程序的每个 Silo 和客户端来应用发布/订阅类型的更改。以协调部署的方式重启，使所有宿主使用相同值并计算相同的订阅集合。

更改模式时，每种订阅模式仍保留自身的生命周期：

- 隐式订阅来自 Grain 元数据。`ExplicitGrainBasedOnly` 选择显式记录，而两种支持隐式订阅的模式均应用匹配的元数据。
- 显式订阅记录遵循配置的 `PubSubStore` 的持久性。`ImplicitOnly` 选择由元数据派生的订阅，并将已保留的显式记录留在存储中。更改模式之前，请考虑这些记录。使用相同的服务 ID、提供程序名称和持久化 `PubSubStore` 重新启用显式订阅支持后，保留的记录将重新可用；随后每个激活的消费者恢复其句柄。

当预计订阅需求将演进，并且持续支持两种模式的收益大于额外的发布/订阅控制平面开销时，请使用默认的组合模式。

<a id="explicit-subscriptions"></a>

### 显式订阅

<a id="recovering-from-failures"></a>
<a id="writing-subscription-logic"></a>
<a id="fully-managed-and-reliable-streaming-pub-sub"></a>

当应用程序行为决定 Grain 或客户端是否以及何时订阅时，使用显式订阅。每次调用 <xref:Orleans.Streams.IAsyncObservable`1.SubscribeAsync*> 都会创建新订阅，因此激活代码必须恢复现有句柄，而不是再次订阅：

:::code language="csharp" source="snippets/streaming/ExplicitSubscriptions.cs" id="explicit_subscription_grain":::

订阅属于 Grain 标识，而不属于单个激活实例。停用后，后续激活实例调用 <xref:Orleans.Streams.IAsyncStream`1.GetAllSubscriptionHandles*> 和 <xref:Orleans.Streams.StreamSubscriptionHandle`1.ResumeAsync*> 以附加新的观察者实例。调用 <xref:Orleans.Streams.StreamSubscriptionHandle`1.UnsubscribeAsync*> 移除订阅。

只有当配置的 [`PubSubStore` 具有持久性](pubsub-storage.md) 时，此生命周期才能跨集群重启保留。内存型 `PubSubStore` 只在集群状态仍可用时保留记录。

对于可回溯的持久化流提供程序，[选择订阅起始位置](subscription-start-positions.md)，以便从新消息开始，或重放拉取代理本地队列缓存中保留的匹配消息。

<a id="end-an-explicit-subscription"></a>

#### 结束显式订阅

通过等待每个句柄的 <xref:Orleans.Streams.StreamSubscriptionHandle`1.UnsubscribeAsync*> 完成来结束订阅。流运行时会从发布/订阅存储中移除每个订阅，并在操作完成之前通知活动生产者。示例的 `UnsubscribeAsync` 方法遵循此顺序。

<a id="implicit-subscriptions"></a>

### 隐式订阅

当流数据项应激活由流标识确定的 Grain 时，使用隐式订阅：

:::code language="csharp" source="snippets/streaming/ImplicitSubscriptions.cs" id="implicit_subscription_grain":::

<xref:Orleans.ImplicitStreamSubscriptionAttribute> 选择流命名空间。对于每个匹配的 Grain 类型，Orleans 将流键映射为 Grain 键。实现 <xref:Orleans.Streams.Core.IStreamSubscriptionObserver> 使 Orleans 提供隐式订阅句柄；调用一次 `ResumeAsync` 以附加处理逻辑。

隐式订阅在观察者保持附加期间单调向前推进。投递调用成功完成后，`ResumeAsync` 接受 `null` 来替换观察者，并拒绝会重新定位此活动订阅的序列令牌。新激活的 Grain 可以使用持久化的恢复令牌附加新提供的句柄。当应用程序逻辑有意重新定位投递位置或重放此前已确认的事件时，应使用显式订阅。

隐式订阅在 Grain 元数据中声明。它们不是通过 <xref:Orleans.Streams.IAsyncObservable`1.SubscribeAsync*> 创建的，无法在运行时单独移除，也不支持对同一个 Grain 绑定创建多个订阅。

<a id="clients"></a>

## 客户端

<a id="grains-and-orleans-clients"></a>
<a id="configuration"></a>

在 <xref:Orleans.Hosting.IClientBuilder> 上配置提供程序后，客户端可以生产流并进行显式消费。客户端订阅与已连接的客户端进程绑定，重新连接或重启后必须重新建立。隐式订阅以 Grain 为目标，而不是客户端。

<a id="stateless-worker-grains"></a>

## 无状态工作者 Grain

标记了 <xref:Orleans.Concurrency.StatelessWorkerAttribute> 的 Grain 可以发布和消费流。无状态工作者流消费者实现 <xref:Orleans.Streams.Core.IStreamSubscriptionObserver>。Orleans 为每个激活实例安装单独的流消费者扩展。投递使用该激活实例中此订阅现有的观察者。如果未附加观察者，则 Orleans 调用 `OnSubscribed`，而 Grain 在该回调中调用 `ResumeAsync` 来附加观察者。

订阅属于无状态工作者 Grain 标识。对于持久化流，每个拉取代理从其所在 Silo 通过该标识投递，普通无状态工作者放置策略为每次投递尝试选择一个本地激活实例。因此，并发拉取代理可以在不同激活实例和 Silo 上处理数据项。每次投递尝试都在一个激活实例上运行，为解码、验证、丰富数据、筛选和转发等无状态转换提供竞争消费者式执行。

隐式订阅根据 Grain 元数据建立 Grain 级订阅。显式 `SubscribeAsync` 调用在运行时建立 Grain 级订阅，并附加调用方激活实例的观察者。后续激活实例在首次收到投递时通过 `OnSubscribed` 附加其本地观察者，而 `UnsubscribeAsync` 则移除 Grain 级订阅。

顺序保证的范围是所选激活实例及提供程序投递路径。不同激活实例上的并发投递可以按任意顺序完成。提供程序重试时可能选择其他激活实例，因此处理程序应采用无状态或幂等处理方式，并遵循提供程序的投递保证。未实现 <xref:Orleans.Streams.Core.IStreamSubscriptionObserver> 而订阅的无状态工作者会收到 <xref:System.InvalidOperationException>。

无状态工作者观察者使用 null 序列令牌进行附加。当投递在激活实例之间移动时，拉取代理负责活动订阅的进度。向 `SubscribeAsync` 或 `ResumeAsync` 传入非 null 序列令牌会产生 <xref:System.InvalidOperationException>。需要由应用程序管理回溯或从检查点恢复时，请使用普通 Grain 消费者。

<a id="stream-order-and-sequence-tokens"></a>
<a id="rewindable-streams"></a>

有关故障行为和序列令牌，继续阅读[投递、排序、重放与恢复](delivery-semantics.md)。

如需更完整的、经过编译的示例，参见 Orleans 测试套件中的 [`SampleStreamingGrain.cs`](https://github.com/dotnet/orleans/blob/main/test/Grains/TestGrains/SampleStreamingGrain.cs)。
