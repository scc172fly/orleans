---
title: "Orleans 流出的API"
description: "与 Orleans中流特性、生产者、消费者和明示或默示订阅者合作。"
ms.date: 08/24/2026
ms.topic: concept-article
---

<a id="orleans-streaming-apis"></a>

# Orleans 流出的API

<a id="stream-identity"></a>

## 流流身份

<a id="async-stream"></a>

三个应用级别选择选择了 Orleans 流:

- **提供者名称** 选择已配置的 <xref:Orleans.Streams.IStreamProvider>。
- **流命名空间** 组相关流,并参与隐含订阅匹配。
- **流键** 识别命名空间内的一条流,可以是字符串、图形用户界面D或整数。

名称空间和密钥窗体 <xref:Orleans.Runtime.StreamId>。将身份构建保留在共享代码中:

:::code language="csharp" source="snippets/streaming/BasicStreaming.cs" id="stream_identity":::

<xref:Orleans.Streams.IStreamProvider.GetStream*> 返回一个已输入的 <xref:Orleans.Streams.IAsyncStream`1>。获取提供者和流控器是本地的,不会创建中介实体。生产者和消费者必须就 `T`达成一致; Orleans 序列化规则适用于有效载荷。

<a id="producers"></a>

## 生产者

<a id="producing-and-consuming"></a>

任何 grain 或配置的 Orleans 客户端都可以发布。 一个流可以有多个制片人:

:::code language="csharp" source="snippets/streaming/BasicStreaming.cs" id="stream_producer":::

发布订单时,请等待每次 <xref:Orleans.Streams.IAsyncObserver`1.OnNextAsync*> 呼叫。返回的任务报告供应商接受服务,而不是端对端消费者完成。提供方失败和含糊的超时要求针对申请重新研究和重复决定。

<a id="consumers"></a>

## 消费者消费者

<a id="multiplicity"></a>

消费者附加 <xref:Orleans.Streams.IAsyncObserver`1> 执行或回调代表。 `OnNextAsync` 接收物品时,如果提供者供应物品,则a <xref:Orleans.Streams.StreamSequenceToken>.

持续供应商使用完成该设备提前交付或失败后重新尝试。避免堵住线条;非同步的消费者工作自然会对该订阅施加回压。

串流是多播的。 每个订阅都接收每个项目, 一个 grain 可以为同一串流创建多个明确的订阅。 每个订阅都有自己的 <xref:Orleans.Streams.StreamSubscriptionHandle`1>。

<a id="explicit-and-implicit-subscriptions"></a>

## 明确和默示订阅

供货商的 <xref:Orleans.Streams.StreamPubSubType> 控制有订阅模式:

| 价值值值 | 选择此值 | 权衡平衡 |
|---|---|---|
| `ExplicitGrainBasedAndImplicit` (违约) | 应用程序使用两种模式,或期望其订阅要求会发生变化。 | 生产者登记检查以 grain为基础的会合和隐含 grain 元数据,而明确部分要求有一个 [`PubSubStore`](pubsub-storage.md)。 |
| `ExplicitGrainBasedOnly` | 每个消费者都使用实时创建的订阅,包括客户消费者。 | 订阅和生产者更改使用会合 grains 和 `PubSubStore`,应用程序管理订阅处理和回收。 |
| `ImplicitOnly` | 每个消费者都是 grain 申报的 <xref:Orleans.ImplicitStreamSubscriptionAttribute>。 | 集束使用 grain 元数据是其订阅目录,零会合-grain 呼叫次数和零 `PubSubStore` 操作。这使它成为公用/次控下飞机管理费最低的,并且无论在哪个元数据定义的地方都具有吸引力 grain 运行时间创建、个人可移动和客户可订阅使用明确能力模式。 |

模式决定订阅的发现、协调和储存工作。 事件运输和交付遵循选定的流提供者。 专门模式可以减少控制机管理费; 根据所需订阅语义选择一种模式, 并衡量应用程序工作量的影响 。

<a id="change-the-configured-mode"></a>

### 更改配置模式

应用一个酒吧/子类型更改, 更新 <xref:Orleans.Hosting.PersistentStreamConfiguratorExtensions.ConfigureStreamPubSub*> 并重新启动每个 silo 和客户端, 用户使用指定提供者。 重新启用它们作为协调部署, 这样每个主机都使用相同的值, 计算同样的订阅集 。

每一订阅模式都保留了自己的生命周期,

- 隐含订阅来自 grain 元数据。 `ExplicitGrainBasedOnly` 选择明确的记录,而两种隐含功能模式都采用对应的元数据。
- 明确的订阅记录遵循配置的 `PubSubStore` 耐久性。 `ImplicitOnly` 选择元数据生成订阅,并在存储处保留明确记录。在改变模式之前将这些记录记账。以相同的服务编号、供应商名称和耐用 `PubSubStore` 重新授权明确支持,再次提供留存记录;每个活跃消费者然后恢复处理。

当订阅要求预期会发生变化,而且对这两种模式的持续支持超过额外的公用/子控制机的工作时,使用默认组合模式。

<a id="explicit-subscriptions"></a>

### 明确订阅次数

<a id="recovering-from-failures"></a>
<a id="writing-subscription-logic"></a>
<a id="fully-managed-and-reliable-streaming-pub-sub"></a>

当应用程序行为决定是否以及何时订阅 grain 或客户端时,使用明确的订阅。 <xref:Orleans.Streams.IAsyncObservable`1.SubscribeAsync*> 每次创建新的订阅,因此激活代码必须恢复现有的控件而不是再次订阅:

:::code language="csharp" source="snippets/streaming/ExplicitSubscriptions.cs" id="explicit_subscription_grain":::

订阅属于 grain 身份, 而不是一个激活。 在禁用后, 稍后的激活呼叫 <xref:Orleans.Streams.IAsyncStream`1.GetAllSubscriptionHandles*> 和 <xref:Orleans.Streams.StreamSubscriptionHandle`1.ResumeAsync*> 附加其新的观察实例。 请打电话 <xref:Orleans.Streams.StreamSubscriptionHandle`1.UnsubscribeAsync*> 删除订阅 。

只有配置 [`PubSubStore` 是耐久性](pubsub-storage.md)时,才在整个集组中保持这一生命周期。只有该集组状态仍然可用时,内存 `PubSubStore` 才能保存记录。

对于可倒放的持久性提供者, [选择订阅启动位置](subscription-start-positions.md) 将开始使用新信件或重放拖拉机本地队列缓存中保留的匹配信件。

<a id="end-an-explicit-subscription"></a>

#### 终止明确订阅

结束订阅时等待每个控点的 <xref:Orleans.Streams.StreamSubscriptionHandle`1.UnsubscribeAsync*> 。流序运行时间将从酒吧/子存储处删除每次订阅,并在操作完成前通知活跃的制作人。示例中的 `UnsubscribeAsync` 方法遵循此顺序。

<a id="implicit-subscriptions"></a>

### 暗隐订阅

当流项目激活由流特性确定的 grain 时使用默认订阅:

:::code language="csharp" source="snippets/streaming/ImplicitSubscriptions.cs" id="implicit_subscription_grain":::

<xref:Orleans.ImplicitStreamSubscriptionAttribute> selects stream namespaces. For each matching grain type, Orleans maps the stream key to the grain key. Implementing <xref:Orleans.Streams.Core.IStreamSubscriptionObserver> lets Orleans supply the implicit handle; call `ResumeAsync` once to attach processing logic.

默认订阅在观察员随行时单调提前。在发送通知成功完成后, `ResumeAsync` 接受 `null` 替换观察员并拒绝将重新定位实际订阅的序列符号。新激活的 grain 可以使用持续恢复标记附加其新提供的处理器。如果应用逻辑故意重新定位交付或重播先前确认的事件,则使用明确的订阅。

隐含订阅在 grain 元数据中声明。 它们不是通过 <xref:Orleans.Streams.IAsyncObservable`1.SubscribeAsync*>创建的, 无法在运行时单独删除, 并且不支持同一 grain 绑定的多个订阅 。

<a id="clients"></a>

## 客户客户

<a id="grains-and-orleans-clients"></a>
<a id="configuration"></a>

客户可在供应商配置在 <xref:Orleans.Hosting.IClientBuilder>后生产和明确消耗流。客户订阅与相关客户程序挂钩,必须在重新连接或重新启动后重新建立。隐性订阅目标 grains,而不是客户。

<a id="stateless-worker-grains"></a>

## 无国籍工人 grains

Grains 标记为 <xref:Orleans.Concurrency.StatelessWorkerAttribute> 无国籍工人流消费者工具 <xref:Orleans.Streams.Core.IStreamSubscriptionObserver>. Orleans 安装单独的串流消费者扩展程序, 每次激活时都安装一个。 发送时使用当前启动的观察员来订阅。 如果没有连接到任何观察者, Orleans 调来电话 `OnSubscribed`和 grain 调来电话 `ResumeAsync` 从调回到附件 。

订费属于无国籍工人 grain 对于持久性流,对于持久性流,每个拉动剂都从其 silo,正常的无国籍工人安置为每次运送尝试选择一个本地启动。因此同时同时拉动剂可以处理不同激活和操作的项目。 silos每次运送尝试都进行一次启动,为诸如解码、验证、浓缩、过滤和转发等无国籍变换提供相互竞争的消费者执行。

隐含订阅建立了 grain- 来自 grain 元数据的级别订阅。 明确的 `SubscribeAsync` 调用在运行时建立 grain- 级别订阅并附加拨号激活的观察员。 稍后的启用会将当地观察员从交货首次到达时通过 `OnSubscribed` 连接到当地, `UnsubscribeAsync` 取消 grain- 级别订阅。

订单的范围扩大到选定的激活和提供方交付路径。同时交付可以按不同启动的顺序完成。供应商可选择不同的激活,因此处理者使用无国籍或无能力处理并遵循提供者的交付保证。未执行 <xref:Orleans.Streams.Core.IStreamSubscriptionObserver> 就订阅的无国籍工人可获得 <xref:System.InvalidOperationException>。

无国籍工人观察家附加无效序列符号。 当发货在激活之间移动时,拖动代理拥有实时订阅的进展。 将非null序列符号传递给 `SubscribeAsync` 或 `ResumeAsync` 产生一个 <xref:System.InvalidOperationException>。 在需要恢复应用管理回转或检查站时使用常规的 grain 消费者。

<a id="stream-order-and-sequence-tokens"></a>
<a id="rewindable-streams"></a>

对于失败行为和序列符号, 继续到 [交付、订购、重播和复原](delivery-semantics.md)。

较大的汇编示例,见 [`SampleStreamingGrain.cs`](https://github.com/dotnet/orleans/blob/main/test/Grains/TestGrains/SampleStreamingGrain.cs) 中在 Orleans 测试套房。
