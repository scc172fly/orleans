---
title: Orleans 观察者
description: 从 Grain 向客户端或其他 Grain 发送异步通知。
ms.date: 08/07/2026
ms.topic: concept-article
---

<a id="orleans-observers"></a>
# Orleans 观察者

观察者使 Grain 能够调用由 Orleans 客户端或另一个 Grain 承载的对象。它们适合在接收方保持连接时发送实时的、尽力而为的通知。

应将观察者用于一小组已知且在线的回调目标。观察者有意采用临时机制，并非持久化事件总线的通用替代方案。客户端可能在没有通知的情况下断开连接，重新创建的观察者会具有不同的标识。如果订阅或投递必须在故障后继续有效，或者需要向许多独立消费者分发消息，应使用 [Orleans Streams](../streaming/index.md) 或其他持久化消息机制。

<a id="when-to-choose-observers-versus-streams"></a>
## 何时选择观察者或流

当回调目标是特定的已连接客户端对象或 Grain，且应用程序只需要在连接保持活动时发送实时通知，可使用 Grain 观察者。观察者所需的基础设施很少：注册回调目标，由 Grain 直接通知该目标。

当应用程序需要多播投递、动态订阅、回放、提供程序定义的持久性，或在客户端或 Grain 重启后恢复时，可使用 Orleans 流。流的生存期可以超越单个激活实例或连接，而观察者注册绑定于回调目标，重新连接后必须重新建立注册。

二者的取舍主要在于生存期和投递语义：

- 观察者是开销较低的直接回调，最适合临时状态或 UI 更新，但不提供持久性、重放能力，也不会在断开连接后自动恢复。
- 流需要配置提供程序并遵循其特定投递语义，因此增加运维成本。显式订阅也可能需要持久化订阅记录。作为交换，流支持独立订阅者、保留的事件以及更灵活的故障恢复。

有关更全面的选型指导，请参阅[选择 Orleans 消息传递抽象](../streaming/streams-why.md)和 [Orleans 流处理 API](../streaming/streams-programming-apis.md)。

<a id="define-an-observer"></a>
## 定义观察者

观察者接口继承自 <xref:Orleans.IGrainObserver>：

:::code language="csharp" source="../snippets/compiled/Grains/ServicesAndObserversSnippets.cs" id="chat_observer":::
使用异步返回类型，避免 `async void`。仅当通知明确采用尽力而为的方式，且发布者无需知道异常或完成情况时，才应用 <xref:Orleans.Concurrency.OneWayAttribute>。

<a id="create-and-remove-a-client-observer-reference"></a>
## 创建与移除客户端观察者引用

将本地对象转换为可寻址引用：

:::code language="csharp" source="../snippets/compiled/Grains/ServicesAndObserversSnippets.cs" id="subscribe_observer":::
在本地观察者需要接收调用的整个期间，保留对该观察者的强引用。不再需要时，取消订阅并删除对象引用：

:::code language="csharp" source="../snippets/compiled/Grains/ServicesAndObserversSnippets.cs" id="unsubscribe_observer":::
删除引用会释放客户端注册。未删除长期存在的注册可能造成资源泄漏。

<a id="manage-subscriptions-in-a-grain"></a>
## 在 Grain 中管理订阅

<xref:Orleans.Utilities.ObserverManager`1> 跟踪观察者，使过期条目失效，并移除通知失败的观察者：

:::code language="csharp" source="../snippets/compiled/Grains/ServicesAndObserversSnippets.cs" id="chat_room_observer_manager":::
当前 API 为 <xref:Orleans.Utilities.ObserverManager`2.Notify*>，其中包括接受返回 <xref:System.Threading.Tasks.Task> 的 <xref:System.Func`2> 的重载。没有 `NotifyAsync` 方法。

订阅在 <xref:Orleans.Utilities.ObserverManager`2.ExpirationDuration> 所指定的时长后以惰性方式过期。客户端应在过期前续订。通知发生异常时，<xref:Orleans.Utilities.ObserverManager`1> 会移除该观察者；并不会导致整个发布操作失败。

如果订阅标识需要与观察者引用不同，请使用 <xref:Orleans.Utilities.ObserverManager`2>。

<a id="grain-observers"></a>
## Grain 观察者

Grain 可以实现观察者接口并传递自身的引用：

:::code language="csharp" source="../snippets/compiled/Grains/ServicesAndObserversSnippets.cs" id="subscribe_grain_observer":::
不要对 Grain 调用 <xref:Orleans.IGrainFactory.CreateObjectReference*>。Grain 本身已经可以被寻址。

<a id="execution-and-cancellation"></a>
## 执行与取消

对同一客户端观察者引用的调用按顺序执行，不可重入。不同观察者引用上的调用可以并发执行。

观察者方法可以接受 <xref:System.Threading.CancellationToken> 参数。取消仍然是协作式的，并不会使观察者投递具有持久性。有关取消语义，请参阅[取消 Orleans Grain 调用](cancellation-tokens.md)。
