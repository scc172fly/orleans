---
title: "与 IAsyncEnumerable相对应的流"
description: "流流 a grain 使用 IAsyncEnumerable<T>."
ms.date: 09/04/2026
ms.topic: concept-article
---

<a id="response-streaming-with-iasyncenumerable"></a>

# 与 IAsyncEnumerable相对应的流

**应对流** 允许 a grain 方法返回 <xref:System.Collections.Generic.IAsyncEnumerable`1> 来电者可以消费一个逻辑单 grain 呼叫的响应会逐渐递增。 grain 任何其他 grain 并提取结果 当它们产生。

使用响应流来进行结果自然递增的询问或命令。 响应流不会创建持久订阅、 保留结果或将结果多投给其他消费者。 对于这些能力, 请考虑 [Orleans 流](../streaming/index.md)。

<a id="define-and-implement-a-response-streaming-method"></a>

## 界定和实施应急流出方法

声明 <xref:System.Collections.Generic.IAsyncEnumerable`1> 直接在 grain 接口。取消的标号是可选的,与其他 grain 方法 :

:::code language="csharp" source="snippets/async-enumerable-results/StreamingGrain.cs" id="streaming_contract":::

同步循环器可以用 `yield return`生成每个结果。应用 <xref:System.Runtime.CompilerServices.EnumeratorCancellationAttribute> ,这样迭代器在调用者列举请求取消时会观察到请求的取消:

:::code language="csharp" source="snippets/async-enumerable-results/StreamingGrain.cs" id="streaming_implementation":::

<a id="consume-a-streamed-response"></a>

## 包含流解响应

使用 `await foreach` 处理每个结果。 响应流在调用者要求第一个元素时开始, 而不是当 grain 方法返回可量化的:

:::code language="csharp" source="snippets/async-enumerable-results/StreamingConsumer.cs" id="consume_stream":::

留有 `await foreach` 环会处理其分子,包括当循环退出时使用 `break` 或例外。

<a id="control-response-batching"></a>

## 控制反应批量

Orleans 批次同步提供元素,以减少网络回程,默认情况下最多100个元素。 请使用 <xref:Orleans.Runtime.AsyncEnumerableExtensions.WithBatchSize*> 修改这一限制:

:::code language="csharp" source="snippets/async-enumerable-results/StreamingConsumer.cs" id="configure_batch_size":::

调调 `WithBatchSize` 直接对返回的数值 grain 包装之前的方法,例如 <xref:System.Threading.Tasks.TaskAsyncEnumerableExtensions.WithCancellation*>。在另一个操作员包扎了可数数字后, `WithBatchSize` 没有无 Orleans 配置请求,且无效。 `1` 要求时发送一个元素 。

条纹不会导致 Orleans 读取未限制的元素数量。调用者下次 `MoveNextAsync` 请求制作驱动器,而批次只包含同步可用、可达到配置限制的元素。

<a id="cancel-response-streaming"></a>

## 取消响应流流

将拨打电话者的信条传给 grain 方法或作为方法参数,或通过 `WithCancellation` ,因此取消停止了自动同步计算器。 Orleans 链接不同的符号,这样可以取消其中任何一个停止点数。 `WithBatchSize` 之前 `WithCancellation`:

:::code language="csharp" source="snippets/async-enumerable-results/StreamingConsumer.cs" id="cancel_stream":::

取消是合作性的,对呼叫者来说是 <xref:System.OperationCanceledException>。反应流方法必须观察其符号并将其传递给取消-认知操作。交付和失败语义见 [取消 Orleans grain 调用](cancellation-tokens.md) 。

<a id="handle-an-interrupted-response-stream"></a>

## 处理中断响应流

制作响应流时被抛出例外,则以其原始例外类型向呼叫者传播。 <xref:Orleans.Runtime.EnumerationAbortedException> 如果 grain 查点期间或计分时停止使用 silo 删除调用者闲置留下的计数器:

:::code language="csharp" source="snippets/async-enumerable-results/StreamingConsumer.cs" id="handle_interruption":::

空数清除器定期使用 <xref:Orleans.Configuration.MessagingOptions.ResponseTimeout> 间隔运行。 不要在做无关的长期工作时将一个数字员打开。 如果处理元素需要很长时间, 则要与拉动下一个元素或使用信息抽象化发生脱钩, 且其寿命不依赖于一个 grain 调用 。

<a id="choose-between-response-streaming-and-orleans-streams"></a>

## 在响应流和 Orleans 串流之间选择

| 关注问题 | 与 `IAsyncEnumerable<T>` 相对应的流  | Orleans 流 |
|---|---|---|
| 通信形状 | 逻辑上说,一个 grain 呼叫,一个生产商和一个打电话者 | 由独立制片人和订户组成的多播公共/子 |
| 寿命 | 一次现场点查,以完成、处置、取消、停止使用或闲置清理结束 | 独立于任何 grain 呼叫;订阅可以维持激活变化 |
| 流动控制 | 以拉式制动; `MoveNextAsync` 驱动器生产,并配有捆绑的批量 | 依赖提供方的交付和缓冲 |
| 持久性和重放 | 无无无无 | 可选和依赖提供方 |
| 最合适 | 递递返回一个命令或查询结果 | 发布多期或长年订阅的出版活动 |

观察员、广播频道和其他替代节目见 [选择 Orleans 短信抽象](../streaming/streams-why.md) 。
