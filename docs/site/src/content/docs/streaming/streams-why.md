---
title: 选择 Orleans 消息传递抽象
description: 在 Grain 调用、响应流式传输、观察者、Orleans 流以及广播通道之间选择。
ms.date: 08/08/2026
ms.topic: concept-article
---

<a id="choose-an-orleans-messaging-abstraction"></a>

# 选择 Orleans 消息传递抽象

从发送方与接收方的关系，以及应用程序需要的故障行为出发进行选择。

| 使用场景 | 最合适的抽象 | 重要行为 |
|---|---|---|
| 调用已知 Grain 并等待结果 | **Grain 调用** | 具有 Orleans 调用语义的定址请求/响应。调用方知道目标 Grain 标识。 |
| 逐步返回一次 Grain 调用的结果 | **响应流式传输（`IAsyncEnumerable<T>`）** | 基于拉取、面向单个调用方的枚举。不支持多播、保留或持久化。 |
| 从 Grain 向已连接的客户端推送临时通知 | **Grain 观察者** | 临时客户端回调。应用程序负责注册和移除观察者引用，并处理断开连接。 |
| 向多个独立订阅发布带类型的事件 | **Orleans 流** | 多播发布/订阅。提供程序选择决定持久性、重试、排序和重放。显式订阅可跨激活实例变化保留。 |
| 向根据通道标识选出的 Grain 发送尽力而为的通知 | **广播通道** | 隐式、非持久化扇出。不提供队列、历史记录、重放或持久化订阅注册表。 |

<a id="prefer-grain-calls-for-commands-and-queries"></a>

## 对于命令和查询优先使用 Grain 调用

当发送方知道哪个 Grain 负责该操作、需要返回值，或需要通过调用传播故障时，使用 Grain 调用。Grain 调用使所有权及控制流明确。不要仅仅为了避免调用已知 Grain 而引入流。

当一次调用产生多个结果时，使用**响应流式传输**：Grain 方法返回 <xref:System.Collections.Generic.IAsyncEnumerable`1>，使调用方能够逐步处理响应。参见[使用 IAsyncEnumerable 进行响应流式传输](../grains/response-streaming.md)。

<a id="prefer-observers-for-client-callbacks"></a>

## 对于客户端回调优先使用观察者

已连接的 Orleans 客户端需要临时回调且可以在重新连接后重新注册时，使用 Grain 观察者。有关支持的生命周期及注册模型，参见 [Orleans 观察者](../grains/observers.md)。观察者并非必然冗余：它为少量存活客户端对象提供低开销的直接回调 API，而流是适合多播事件流及提供程序特定投递语义的抽象。

当生产者需要向许多独立订阅者扇出、投递必须跨客户端断开连接或 Grain 重新激活保留，或者提供程序支持重放、保留或持久化订阅状态时，请改用流。观察者引用不是持久化订阅，不能替代保留事件或服务端发布/订阅系统。

<a id="prefer-streams-for-multicast-event-flow"></a>

## 对于多播事件流优先使用流

流将生产者和消费者的标识、时间及放置位置解耦。适用于按实体组织的事件源、动态订阅、外部消息中间件集成，以及 Grain 中的有状态事件处理。

Orleans 流是多播流，不是点对点工作队列：对逻辑流的每个订阅都会接收数据项。提供程序可以对物理队列分区以实现扩展，但这不会改变逻辑扇出模型。如果每个任务必须由唯一的工作者领取，请显式建模其所有权，而不是假定存在竞争消费者语义。

提供程序是设计的一部分。持久化队列可以跨进程故障保留已接受的事件；可回溯的提供程序可以从较早的令牌位置开始订阅。Orleans 流 API 本身不隐含这两种能力。

<a id="why-orleans-streams-are-different"></a>

## Orleans 流的独特之处

Orleans 流与事件中间件及数据流引擎相互补充，而非取代它们。事件中间件保留并传输事件，数据流引擎擅长对大型事件集合应用共享查询或转换流水线。当每个实体都需要独立寻址的有状态处理，并且希望使用普通 Grain 代码表达这种逻辑时，Orleans 流非常有用。

<a id="flexible-processing-logic"></a>

### 灵活的处理逻辑

<a id="flexible-stream-processing-logic"></a>

流消费者是应用程序代码。它可以更新 Grain 状态、调用 Grain、发布到其他流、调用外部服务，或根据 Grain 当前状态选择行为。处理可以是命令式或函数式、有状态或无状态，并且可以包含副作用。

Orleans 流不提供声明式查询语言，也不会自动编译数据流图。当主要需求是窗口计算、连接、聚合或集中管理的查询拓扑时，应使用专用流处理引擎。当事件必须进入具有各实体独立状态及行为的细粒度 Actor 工作流时，使用 Orleans 流。

<a id="dynamic-fine-grained-topologies"></a>

### 动态、细粒度的拓扑

<a id="support-for-dynamic-topologies"></a>
<a id="fine-grained-stream-granularity"></a>

处理拓扑由流订阅及 Grain 逻辑形成，而不是一个覆盖整个部署的图。应用程序可以在运行时添加或移除显式订阅，通过隐式订阅根据流标识激活 Grain，并在单个 Grain 的状态变化时改变其响应方式。

流通过提供程序、命名空间和键进行独立寻址。Grain 可以消费或生产多个流，应用程序也可以根据持久性、重放能力、吞吐量和运维要求为不同链路选择不同提供程序。这种粒度支持按用户、设备、租户等实体组织的事件流，而无须为每个实体部署独立流水线。

<a id="distributed-execution"></a>

### 分布式执行

流消费者是 Grain，因此其处理工作通过 Orleans 运行时分布式执行。应用程序可以扩展集群、将流标识分布到不同 Grain、在故障后恢复 Grain 激活实例，并将流处理与 Orleans 的放置、持久化及消息传递结合起来。

这些特性并不能免除针对提供程序特定投递保证、排序、重放、背压以及热点键的设计工作。参见[投递、排序、重放与恢复](delivery-semantics.md) 和[运维和调优 Orleans 流](streaming-operations.md)。

<a id="prefer-broadcast-channels-for-transient-fan-out"></a>

## 对于临时扇出优先使用广播通道

广播通道适用于缓存失效提示、实时配置通知，以及其他可接受偶尔丢失的类似信号。通道键为每个匹配的订阅者 Grain 类型映射出一个订阅者 Grain 标识；它并不向集群中的每个激活实例广播。

当消费者需要持久化订阅记录、保留事件、消费者故障后的重试或重放时，应改用流。有关完整标识及投递模型，参见[广播通道](broadcast-channel.md)。
