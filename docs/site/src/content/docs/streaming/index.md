---
title: "与 Orleans串流"
description: "理解 Orleans 流,选择一个 Orleans 应用程序的右线电文抽象。"
ms.date: 08/02/2026
ms.topic: overview
---

<a id="streaming-with-orleans"></a>

# 与 Orleans串流

<a id="why-should-i-care"></a>

Orleans 流是输入的、符合逻辑的、多播频道。制片人和消费者用提供者的名称和 <xref:Orleans.Runtime.StreamId>来回答一个流,他们不需要相互引用。流可以有很多制作人和许多订阅者,每个已出版的项目都提供给该流上的每一个订阅者。

流是一个虚拟地址。 它的传输、 保留、 重试行为、 命令和重播能力由配置 [串流提供商](stream-providers.md)提供。 流控单靠一个流控无法使数据持久 。

当事件向独立管理的消费者传播时,当订阅时间比 grain 激活时间长时,或者当外部队列或事件日志应填入 grains时,使用流。对于请求/回复电话、特定的连接客户回击或低管理端最佳扇门外出,另一个 Orleans 短信抽象信息可能更合适。直接客户回调请见 [grain 观察员](../grains/observers.md) ,完整决定模型见 [选择 Orleans 短信抽象](streams-why.md) 。

<a id="programming-model"></a>

## 方案拟订模式

- <xref:Orleans.Runtime.StreamId> 由命名空间和密钥组成。服务商名称选择配置的运输方式。
- <xref:Orleans.Streams.IAsyncStream`1> 既是一个生产者又是一个消费者的处理器。获得一个处理器是当地的业务;出版和订阅进行工作。
- 串流是多播的。 多制片人可以发布到一个流, 每个活动订阅都接收每个项目 。
- 订阅可以是 [明示或默示](streams-programming-apis.md#explicit-and-implicit-subscriptions)。
- 交货担保、订购和重播是 [provider-specific](delivery-semantics.md)。
- 只有在 `PubSubStore` 使用 [耐久 grain 储存](pubsub-storage.md)时,明确的订阅记录才能持久。

<a id="get-started"></a>

## 开始

<a id="quick-start-sample"></a>
<a id="stream-providers"></a>
<a id="stream-semantics"></a>
<a id="code-samples"></a>

1. 与原始提供者一起追逐 [快速启动流](streams-quick-start.md) 。
1. 学习流 [身份、生产、消费和订阅](streams-programming-apis.md)。
1. 选择新的持续流订阅方式 [以现场或本地缓存信件开头](subscription-start-positions.md)。
1. 选择所需的耐久性和重播行为 [提供商](stream-providers.md) 。
1. [交付、订购、重放和回收](delivery-semantics.md)计划。
1. 用于生产 的 配置 [PubSub 储存](pubsub-storage.md) 和 [业务业务](streaming-operations.md) 。

最佳努力不排队或重播,暗含的扇子外出,见 [广播频道](broadcast-channel.md)。

<a id="streams-implementation"></a>

持久性流后面的运行时结构见 [Orleans 流执行](../implementation/streams-implementation/index.md)。
