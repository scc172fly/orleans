---
title: "Orleans 流流快速启动"
description: "配置 Orleans 的模拟流,发布事件,并用默认订阅来消耗它们。"
ms.date: 08/02/2026
ms.topic: quickstart
---

<a id="orleans-streaming-quickstart"></a>

# Orleans 流流快速启动

此快速启动使用 [`Microsoft.Orleans.Streaming`](https://www.nuget.org/packages/Microsoft.Orleans.Streaming) 软件包和内存流提供者。 它不需要外部中介, 但队列数据和 `PubSubStore` 都在记忆中。 使用此配置进行开发和测试, 而不是生产耐久性 。

<a id="configure-the-silo"></a>

## 配置 silo

<a id="required-configurations"></a>

注册生产者和消费者使用的同一供应商名称。 `PubSubStore` 存储明确的流订阅元数据。

:::code language="csharp" source="snippets/streaming/Configuration.cs" id="memory_silo":::

如果外部 Orleans 客户端公布或订阅,该客户端上配置同一供应商:

:::code language="csharp" source="snippets/streaming/Configuration.cs" id="memory_client":::

<a id="define-the-event-and-stream-identity"></a>

## 定义事件和流流身份

流载荷使用正常的 Orleans 序列化模式,该例子还集中了供应商的名称和流名空间,使生产者和消费者构建相同的流特性。

:::code language="csharp" source="snippets/streaming/BasicStreaming.cs" id="stream_contract":::

:::code language="csharp" source="snippets/streaming/BasicStreaming.cs" id="stream_identity":::

流体命名空间是 `device-telemetry`,而流键是设备 ID。有效载荷类型是输入控点的一部分,而不是 <xref:Orleans.Runtime.StreamId>的一部分。

<a id="publish-events"></a>

## 发布事件

<a id="produce-events"></a>

生产者在激活期间获得流控器,并按要求启动 Orleans grain 计时器:

:::code language="csharp" source="snippets/streaming/BasicStreaming.cs" id="stream_producer":::

使用 <xref:Orleans.GrainBaseExtensions.RegisterGrainTimer*> 用于 grain 等待时间 <xref:Orleans.Streams.IAsyncObserver`1.OnNextAsync*> 等到供应商根据其合同承担责任;一般不会等待每个消费者完成。 [生产者确认](delivery-semantics.md#producer-acknowledgment).

<a id="consume-with-an-implicit-subscription"></a>

## 含默示订阅

<a id="subscribe-to-and-receive-streaming-data"></a>

含暗订阅的地图绘制了 grain 键的发布 `device-telemetry/device-17` 激活 `DeviceTelemetryGrain` 其字符串键 `device-17`执行 <xref:Orleans.Streams.Core.IStreamSubscriptionObserver> 观察员提供的观察员的 Orleans:

:::code language="csharp" source="snippets/streaming/ImplicitSubscriptions.cs" id="implicit_subscription_grain":::

以相同的设备键获取 `ITemperatureProducerGrain` 并使用 `StartAsync` 开始发布。 制片人和消费者不相互参照, 他们就供应商名称、 流态身份及事件类型达成一致 。

下,读 [流通的APPS](streams-programming-apis.md),然后替换存储提供者和内存 `PubSubStore` 由贸发会议和生产服务部门以及从 [供应商矩阵](stream-providers.md).
