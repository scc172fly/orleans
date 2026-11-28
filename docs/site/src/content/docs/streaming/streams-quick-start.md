---
title: Orleans 流处理快速入门
description: 配置 Orleans 内存流、发布事件，并通过隐式订阅消费事件。
ms.date: 08/02/2026
ms.topic: quickstart
---

<a id="orleans-streaming-quickstart"></a>

# Orleans 流处理快速入门

本快速入门使用 [`Microsoft.Orleans.Streaming`](https://www.nuget.org/packages/Microsoft.Orleans.Streaming) 包及内存流提供程序。它不需要外部消息中间件，但队列数据和 `PubSubStore` 均位于内存中。此配置用于开发和测试，不用于要求生产环境持久性的场景。

<a id="configure-the-silo"></a>

## 配置 Silo

<a id="required-configurations"></a>

注册生产者和消费者使用的相同提供程序名称。`PubSubStore` 存储显式流订阅元数据。

:::code language="csharp" source="snippets/streaming/Configuration.cs" id="memory_silo":::

如果外部 Orleans 客户端需要发布或订阅，请在该客户端上配置相同的提供程序：

:::code language="csharp" source="snippets/streaming/Configuration.cs" id="memory_client":::

<a id="define-the-event-and-stream-identity"></a>

## 定义事件和流标识

流有效载荷使用常规 Orleans 序列化模型。示例还集中定义提供程序名称及流命名空间，使生产者与消费者构造相同的流标识。

:::code language="csharp" source="snippets/streaming/BasicStreaming.cs" id="stream_contract":::

:::code language="csharp" source="snippets/streaming/BasicStreaming.cs" id="stream_identity":::

流命名空间为 `device-telemetry`，流键为设备 ID。有效载荷类型是强类型句柄的一部分，而不是 <xref:Orleans.Runtime.StreamId> 的组成部分。

<a id="publish-events"></a>

## 发布事件

<a id="produce-events"></a>

生产者在激活期间获取其流句柄，并在收到请求时启动 Orleans Grain 定时器：

:::code language="csharp" source="snippets/streaming/BasicStreaming.cs" id="stream_producer":::

使用 <xref:Orleans.GrainBaseExtensions.RegisterGrainTimer*> 创建 Grain 定时器。等待 <xref:Orleans.Streams.IAsyncObserver`1.OnNextAsync*> 完成意味着等待提供程序按其契约接管数据处理责任；通常并不会等待每个消费者完成处理。参见[生产者确认](delivery-semantics.md#producer-acknowledgment)。

<a id="consume-with-an-implicit-subscription"></a>

## 使用隐式订阅消费事件

<a id="subscribe-to-and-receive-streaming-data"></a>

隐式订阅将流键映射到 Grain 键。向 `device-telemetry/device-17` 发布消息会激活 `DeviceTelemetryGrain`，其字符串键为 `device-17`。实现 <xref:Orleans.Streams.Core.IStreamSubscriptionObserver> 以附加 Orleans 提供的观察者：

:::code language="csharp" source="snippets/streaming/ImplicitSubscriptions.cs" id="implicit_subscription_grain":::

使用相同设备键获取 `ITemperatureProducerGrain`，并调用 `StartAsync` 开始发布。生产者和消费者不直接引用彼此；双方只需约定提供程序名称、流标识和事件类型。

接下来阅读[流处理 API](streams-programming-apis.md)，然后将内存提供程序及内存型 `PubSubStore` 替换为从[提供程序对照表](stream-providers.md) 中选择的生产环境服务。
