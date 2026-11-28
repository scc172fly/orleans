---
title: 使用 Orleans 流
description: 了解 Orleans 流，并为 Orleans 应用程序选择适当的消息传递抽象。
ms.date: 08/02/2026
ms.topic: overview
---

<a id="streaming-with-orleans"></a>

# 使用 Orleans 流

<a id="why-should-i-care"></a>

Orleans 流是具有类型的逻辑多播通道。生产者和消费者通过提供程序名称和 <xref:Orleans.Runtime.StreamId> 寻址流，无需持有彼此的引用。一个流可以有多个生产者和多个订阅，每个发布的项都会被提供给该流上的每个订阅。

流是一个虚拟地址。其传输方式、保留机制、重试行为、顺序及重放能力均由所配置的[流提供程序](stream-providers.md) 决定。仅有流句柄并不意味着数据具有持久性。

当事件需要分发给独立管理的消费者、订阅需要在 Grain 激活实例停用后仍然存在，或者需要由外部队列或事件日志向 Grain 提供数据时，请使用流。对于请求/响应调用、面向特定已连接客户端的回调，或低开销的尽力而为式扇出，其他 Orleans 消息传递抽象可能更适合。有关直接客户端回调，请参阅 [Grain 观察者](../grains/observers.md)；有关完整的选择依据，请参阅[选择 Orleans 消息传递抽象](streams-why.md)。

<a id="programming-model"></a>

## 编程模型

- <xref:Orleans.Runtime.StreamId> 由命名空间和键组成。提供程序名称用于选择已配置的传输方式。
- <xref:Orleans.Streams.IAsyncStream`1> 既是生产者句柄，也是消费者句柄。获取句柄是本地操作；发布和订阅则会执行实际工作。
- 流采用多播方式。多个生产者可以向同一流发布消息，每个活动订阅都会接收每一项。
- 订阅可以是[显式订阅或隐式订阅](streams-programming-apis.md#explicit-and-implicit-subscriptions)。
- 投递保证、顺序和重放能力[取决于提供程序](delivery-semantics.md)。
- 只有 `PubSubStore` 使用[持久化 Grain 存储](pubsub-storage.md) 时，显式订阅记录才具有持久性。

<a id="get-started"></a>

## 入门

<a id="quick-start-sample"></a>
<a id="stream-providers"></a>
<a id="stream-semantics"></a>
<a id="code-samples"></a>

1. 使用内存提供程序完成[流快速入门](streams-quick-start.md)。
1. 了解[流标识、生产、消费和订阅 API](streams-programming-apis.md)。
1. 选择新建持久化流订阅[从实时消息还是本地缓存消息开始](subscription-start-positions.md)。
1. 根据所需的持久性和重放行为选择[提供程序](stream-providers.md)。
1. 规划[投递、顺序、重放和恢复](delivery-semantics.md)。
1. 为生产环境配置 [PubSub 存储](pubsub-storage.md) 和[运维](streaming-operations.md)。

有关无需排队或重放的尽力而为式隐式扇出，请参阅[广播通道](broadcast-channel.md)。

<a id="streams-implementation"></a>

有关持久化流背后的运行时架构，请参阅 [Orleans 流的实现](../implementation/streams-implementation/index.md)。
