---
title: "选择 Orleans 短信抽象"
description: "在 grain 调用、响应流、观察者、 Orleans 流和广播频道中选择。"
ms.date: 08/08/2026
ms.topic: concept-article
---

<a id="choose-an-orleans-messaging-abstraction"></a>

# 选择 Orleans 短信抽象

发件人和接收人之间的关系 以及你申请时的失败行为

| 使用 | 最合适 | 重要行为 |
|---|---|---|
| 调用已知的 grain 等待结果 | **Grain 调用** | 响应请求/答复时使用 Orleans 调用语义,打电话者知道目标 grain 身份。 |
| 逐项返回 grain 要求结果 | **反应流 (`IAsyncEnumerable<T>`)** | 以拉子为基础的单呼数计数, 它不是多播、保留或耐用。 |
| 将从 grains 向连接客户端的瞬时通知按到 | **Grain 观察员** | 用户时间短暂的回调。 应用程序登记并删除观察员引用文件, 处理断开操作 。 |
| 向多个独立订阅者打字 | **Orleans 流** | 多播公共酒吧/子。 提供方的选择控制了耐用性、 重试、 订购和重播。 明确订阅可以维持激活变化 。 |
| 向从频道身份中选择的 grains 发送最佳努力通知 | **广播频道** | 隐含、非持久性的外观。 没有排队、历史、重播或永久订阅登记。 |

<a id="prefer-grain-calls-for-commands-and-queries"></a>

## 更倾向于 grain 呼叫命令和查询

使用 a grain 当发件人知道谁知道 grain 拥有操作,需要回报值,或者无法通过电话传播。 Grain 使用权和控制控制源流明确。 不要仅仅为了避免调用已知的 grain.

当一个呼叫产生许多结果时使用 **响应流** : grain 方法返回 <xref:System.Collections.Generic.IAsyncEnumerable`1> ,以便调用者能够逐步处理该响应。见 [与 IAsyncEnumerable相对应的流](../grains/response-streaming.md)。

<a id="prefer-observers-for-client-callbacks"></a>

## 更希望有观察员来进行客户回召

使用 a grain 连接到观察员 Orleans 客户端想要瞬时回调, 并在重新连接后可以重新注册。 见 [Orleans 观察员](../grains/observers.md) 观察员并非绝对多余:他们是一个低头直接召回 API 对于一小组活客户对象而言,流是多播事件流和具体供应商交付的语义的适当抽象。

选择流, 而不是当生产商需要向许多独立用户发出信息时、 当交付必须维持客户断开或 grain 重新启动时, 或者当供应商提供重播、 保留或持久订阅状态时。 观察员引用不是耐久的订阅, 也不是保留事件或服务器端酒吧/ 子栏的替代。

<a id="prefer-streams-for-multicast-event-flow"></a>

## 多播事件流的偏好于多播事件流

生产者在身份、时间和安置方面与消费者脱钩,他们适合每个实体的活动反馈、动态订阅、与外部经纪人的结合以及有声有色事件处理 grains.

Orleans 流是多播的,而不是点对点的工作队列:每个逻辑串行的订阅都接收项目。一个供应商可以将物理队列分隔为比例,但这不会改变逻辑扇扇出模式。如果只有一个工人必须要求每一项工作,那么就应该明确以所有权的模式代替竞争消费者语义。

供应商是设计的一部分。耐久队列可以保留整个进程故障中公认的事件;可倒带的提供者可以从早期的象征性开始订阅; Orleans 流 API 本身意味着两个能力都没有。

<a id="why-orleans-streams-are-different"></a>

## 为什么 Orleans 不同的串流

Orleans 流补充而不是取代事件经纪人和数据流引擎。 中间商保留和运输事件,而数据流引擎则擅长对大型事件集应用共享查询或转换管道。 Orleans 流在各实体需要独立处理时有用,以普通 grain 代码表示的有声处理。

<a id="flexible-processing-logic"></a>

### 灵活处理逻辑

<a id="flexible-stream-processing-logic"></a>

流式消费者是应用代码。它可以更新 grain 状态, make grain 电话、发布到其他流流、呼叫外部服务或选择行为 grain处理可能是必须的,或者可以起作用的, 状态良好或无国籍, 并且可能包括副作用。

Orleans 流不提供声明性查询语言,也不自动编译数据流程图。在窗口、组合、聚合或中央管理的查询表层时使用专门的流处理引擎是首要要求。当事件必须输入精细区分的行为者工作流程且符合每个实体的状况和行为时,请使用 Orleans 流。

<a id="dynamic-fine-grained-topologies"></a>

### 动态、精细地重选的地形学

<a id="support-for-dynamic-topologies"></a>
<a id="fine-grained-stream-granularity"></a>

处理表层学来自流订阅和 grain 逻辑,而不是一个全部署图。 应用程序可以在运行时添加或删除明确的订阅,使用隐含的订阅来激活流特性中的 grains ,并随着状态的变化改变个人 grain 的反应方式。

串流由提供者、命名空间和钥匙独立处理。 A grain 可以消耗或产生多个流,应用程序可以根据不同供应商的耐久性、重放、吞吐量和业务要求使用不同的链接。这种颗粒支持每个用户、每设备、每租户和类似事件的流动,而没有为每个实体单独安装一条管道。

<a id="distributed-execution"></a>

### 分配执行

流用户是 grains, 因此它们的处理使用 Orleans 运行时间进行分配。 应用程序可以扩大集、 在整个 grains中发布流身份、 失败后恢复 grain 启动程序, 并将流处理与 Orleans 放置、 持久性和信息传递结合起来 。

这些属性并不排除设计供应商特定交付保证、订购、重放、后压和热键等需要。 见 [交付、订购、重播和复原](delivery-semantics.md) 和 [运行和调制 Orleans 流](streaming-operations.md)。

<a id="prefer-broadcast-channels-for-transient-fan-out"></a>

## 更可取的即时扇式退出播放频道

广播频道可用于缓存失效提示、现场配置通知和类似信号,有时丢失是可以接受的。 grain 每个匹配的用户身份 grain 类型; 它不会对集的每个激活进行广播 。

当消费者需要耐用订阅记录、保留活动、消费者失败后重试或重播时,使用流。 [广播频道](broadcast-channel.md) 完整身份和交付模式。
