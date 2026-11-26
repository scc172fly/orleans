---
title: "Orleans 观察员"
description: "发送来自 grains 向客户或其他 grains."
ms.date: 08/07/2026
ms.topic: concept-article
---

<a id="orleans-observers"></a>

# Orleans 观察员

观察家让 grains 调用一个 Orleans 客户端或其他 grain主机的对象。这些对象在接收器连接时可用于实时、最佳操作通知。

使用观察者来进行一小组已知的、现场回击。 它们具有故意瞬间性,不是持久事件公交车的一般替代物。 一个客户可以不经通知中断服务,而重新创建的观察员则有不同的身份。 当订阅或发送必须成功或者向许多独立消费者发出信号时,使用 [Orleans 流流](../streaming/index.md) 或其他耐久信息机制。

<a id="when-to-choose-observers-versus-streams"></a>

## 何时选择观察员相对于流

使用 a grain 选项卡中,当回调目标是一个特定连接客户端对象或 grain 观察员几乎没有什么基础设施:登记了一个回调目标,而且 grain 直接通知这一目标。

使用 a Orleans 当应用程序需要多播传送、动态订阅、回放、供应商定义的持久性或客户恢复后, 程序需要多播放发送、 动态订阅、 重播、 服务商定义的耐久性或恢复时的串流 grain 重新启动。 流体可以比单个激活或连接活过一个时间, 而观察员的注册则与回调目标挂钩, 并且必须在重新连接后重建 。

取舍主要与寿命和交付的语义有关:

- 观察者是低头、直接回击的,最有利于短暂状态或UI更新,但不能持久、可重播或断线后自动恢复。
- 串流通过配置的供应商和具体供应商交付语义增加了运营成本。 明确订阅也可以需要耐久的订阅记录。 作为回报,流支持独立用户、保留活动以及更灵活的故障回收。

关于更广泛的决定指导意见,见 [选择 Orleans 短信抽象](../streaming/streams-why.md) 和 [Orleans 流出的API](../streaming/streams-programming-apis.md) 。

<a id="define-an-observer"></a>

## 定义观察员

观察员接口来自 <xref:Orleans.IGrainObserver>:

:::code language="csharp" source="../snippets/compiled/Grains/ServicesAndObserversSnippets.cs" id="chat_observer":::
使用非同步返回类型。 避免 `async void`。 只有当通知是故意尽最大努力,出版商不需要例外或完成时才应用 <xref:Orleans.Concurrency.OneWayAttribute> 。

<a id="create-and-remove-a-client-observer-reference"></a>

## 创建并删除客户端观察员引用

将本地对象转换为可覆盖的引用:

:::code language="csharp" source="../snippets/compiled/Grains/ServicesAndObserversSnippets.cs" id="subscribe_observer":::
强烈提及当地观察员只要其应当收到电话,就应保留该观察员的有力参考。完成后,取消订阅并删除目标引用:

:::code language="csharp" source="../snippets/compiled/Grains/ServicesAndObserversSnippets.cs" id="unsubscribe_observer":::
删除参考文件后,客户端登记就会被解密。 如果不能删除长寿登记,资源就会流失。

<a id="manage-subscriptions-in-a-grain"></a>

## 管理 grain中的订阅

<xref:Orleans.Utilities.ObserverManager`1> 跟踪观察员,过期失效条目,并删除通知不合格的观察员:

:::code language="csharp" source="../snippets/compiled/Grains/ServicesAndObserversSnippets.cs" id="chat_room_observer_manager":::
当前 API 是 <xref:Orleans.Utilities.ObserverManager`2.Notify*>,包括接受 <xref:System.Func`2> 返回的 <xref:System.Threading.Tasks.Task>超载。没有 `NotifyAsync` 方法。

订阅时间在 <xref:Orleans.Utilities.ObserverManager`2.ExpirationDuration>客户应在到期前续延。 <xref:Orleans.Utilities.ObserverManager`1> 将观察者删除; 它不会让整个出版操作失败。

当订阅身份不同于观察员的参考时使用 <xref:Orleans.Utilities.ObserverManager`2> 。

<a id="grain-observers"></a>

## Grain 观察员

grain 可安装观察员界面,并自带一个参考号:

:::code language="csharp" source="../snippets/compiled/Grains/ServicesAndObserversSnippets.cs" id="subscribe_grain_observer":::
别打电话 <xref:Orleans.IGrainFactory.CreateObjectReference*> a 的a grain. Grains 已经可以处理。

<a id="execution-and-cancellation"></a>

## 执行和取消

使用一个客户端观察员的参考文件, 顺序执行, 而不是重新响应。 不同的观察者引用文件可以同时执行 。

观察员方法可以接受 <xref:System.Threading.CancellationToken> 参数。取消仍然合作,无法使观察员发送文件持久。请查看 [取消 Orleans grain 调用](cancellation-tokens.md) 的取消语义。
