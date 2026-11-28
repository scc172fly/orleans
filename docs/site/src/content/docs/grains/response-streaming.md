---
title: 使用 IAsyncEnumerable 进行响应流式传输
description: 使用 IAsyncEnumerable<T> 逐步传输 Grain 调用的响应。
ms.date: 09/04/2026
ms.topic: concept-article
---

<a id="response-streaming-with-iasyncenumerable"></a>
# 使用 IAsyncEnumerable 进行响应流式传输

**响应流式传输**使 Grain 方法能够返回 <xref:System.Collections.Generic.IAsyncEnumerable`1>，让一个调用方逐步消费逻辑上同一次 Grain 调用的响应。调用方与其他 Grain 调用一样寻址目标 Grain，然后随着结果的产生逐步拉取结果。

对于结果天然适合逐步返回的查询或命令，可使用响应流式传输。响应流不会创建持久化订阅、保留结果或将结果多播给其他消费者。如果需要这些功能，请考虑 [Orleans Streams](../streaming/index.md)。

<a id="define-and-implement-a-response-streaming-method"></a>
## 定义并实现响应流式传输方法

直接在 Grain 接口上声明 <xref:System.Collections.Generic.IAsyncEnumerable`1>。与其他 Grain 方法一样，取消令牌为可选参数：

:::code language="csharp" source="snippets/async-enumerable-results/StreamingGrain.cs" id="streaming_contract":::

异步迭代器可以通过 `yield return` 逐个生成结果。应用 <xref:System.Runtime.CompilerServices.EnumeratorCancellationAttribute>，使迭代器能够观察到调用方枚举期间发出的取消请求：

:::code language="csharp" source="snippets/async-enumerable-results/StreamingGrain.cs" id="streaming_implementation":::

<a id="consume-a-streamed-response"></a>
## 消费流式响应

使用 `await foreach` 处理每个结果。响应流在调用方请求第一个元素时启动，而不是在 Grain 方法返回可枚举对象时启动：

:::code language="csharp" source="snippets/async-enumerable-results/StreamingConsumer.cs" id="consume_stream":::

离开 `await foreach` 循环时会释放其枚举器，包括通过 `break` 或因异常退出循环的情况。

<a id="control-response-batching"></a>
## 控制响应的批处理

Orleans 将可同步获取的元素批量发送以减少网络往返次数，默认每批最多 100 个元素。使用 <xref:Orleans.Runtime.AsyncEnumerableExtensions.WithBatchSize*> 更改此上限：

:::code language="csharp" source="snippets/async-enumerable-results/StreamingConsumer.cs" id="configure_batch_size":::

应直接对 Grain 方法的返回值调用 `WithBatchSize`，并在使用 <xref:System.Threading.Tasks.TaskAsyncEnumerableExtensions.WithCancellation*> 等包装操作之前调用。如果可枚举对象已被其他运算符包装，`WithBatchSize` 将无法找到可配置的 Orleans 请求，因而不会生效。批量大小为 `1` 时，每个请求发送一个元素。

批处理不会让 Orleans 提前读取无限数量的元素。调用方的下一次 `MoveNextAsync` 请求驱动元素的生成；每批只包含能够同步获取的元素，且不超过配置的上限。

<a id="cancel-response-streaming"></a>
## 取消响应流式传输

将调用方的令牌作为方法参数或通过 `WithCancellation`（或者两种方式同时使用）传给 Grain 方法，使取消操作能够停止异步枚举器。Orleans 会链接不同的令牌，使任一令牌的取消都能停止枚举。在 `WithCancellation` 之前调用 `WithBatchSize`：

:::code language="csharp" source="snippets/async-enumerable-results/StreamingConsumer.cs" id="cancel_stream":::

取消是协作式的，调用方会收到 <xref:System.OperationCanceledException>。响应流式传输方法必须观察其令牌，并将令牌传给支持取消的操作。有关投递和故障语义，请参阅[取消 Orleans Grain 调用](cancellation-tokens.md)。

<a id="handle-an-interrupted-response-stream"></a>
## 处理中断的响应流

生成响应流时抛出的异常以原始异常类型传播给调用方。如果 Grain 在枚举期间停用，或 Silo 移除了调用方闲置的枚举器，调用方收到的则是 <xref:Orleans.Runtime.EnumerationAbortedException>：

:::code language="csharp" source="snippets/async-enumerable-results/StreamingConsumer.cs" id="handle_interruption":::

空闲枚举器清理会定期运行，周期为 <xref:Orleans.Configuration.MessagingOptions.ResponseTimeout>。不要在进行无关的耗时操作时一直保持枚举器打开。如果处理某个元素可能耗时较长，应将这项工作与拉取下一元素解耦，或者使用生存期不依赖于单次 Grain 调用的消息传递抽象。

<a id="choose-between-response-streaming-and-orleans-streams"></a>
## 在响应流式传输与 Orleans Streams 之间选择

| 关注点 | 使用 `IAsyncEnumerable<T>` 的响应流式传输 | Orleans Streams |
|---|---|---|
| 通信形态 | 逻辑上的一次 Grain 调用，一个生产者和一个调用方 | 多播发布/订阅，生产者和订阅者相互独立 |
| 生存期 | 一次活动的枚举，在完成、释放、取消、停用或空闲清理时结束 | 独立于任何单次 Grain 调用；订阅可以跨激活实例变化继续存在 |
| 流量控制 | 基于拉取；`MoveNextAsync` 驱动生成过程，批量大小有上限 | 投递和缓冲取决于提供程序 |
| 持久化与重放 | 无 | 可选，取决于提供程序 |
| 最适用的场景 | 逐步返回一次命令或查询的结果 | 向多个订阅或长时间存在的订阅发布事件 |

有关观察者、广播通道及其他替代方案，请参阅[选择 Orleans 消息传递抽象](../streaming/streams-why.md)。
