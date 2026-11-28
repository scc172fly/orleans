---
title: "Grain 请求调度"
description: "了解 Orleans 基于执行轮次的执行模型、交错执行与可重入性。"
ms.date: 10/15/2026
ms.topic: concept-article
---

<a id="grain-request-scheduling"></a>

# Grain 请求调度

每个 Grain 激活实例同一时间只执行一个执行轮次（turn）。Orleans 执行一个请求，直到请求完成或遇到尚未完成的 `await`，随后将其后续执行（continuation）调度为另一个执行轮次。同一个激活实例中的两个执行轮次绝不会并行执行。

默认情况下，当前请求尚未完成时，激活实例不会开始执行其他请求。这种不可重入的模型使可变 Grain 状态更容易理解和推理：

:::code language="csharp" source="../snippets/compiled/Grains/RequestsAndVersioningSnippets.cs" id="serialized_counter_grain":::
尽管该方法在 `await` 处让出执行权，但在当前请求完成之前，其他请求不会读取或修改 `_value`。

<a id="avoid-blocking"></a>

## 避免阻塞

绝不要在 Grain 代码中同步阻塞等待尚未完成的任务。`.Result`、`.Wait()`、`WaitAll` 和 `GetAwaiter().GetResult()` 可能使激活实例发生死锁，并占用线程池线程。请使用 `await`。

普通的 `await` 会使 Grain 代码在激活实例的调度器上继续执行。不要在 Grain 方法中使用 `ConfigureAwait(false)`，因为后续执行可能脱离 Grain 调度器。通用库可以在内部使用它；只要 Grain 代码以普通的 `await` 等待该库的操作，后续执行就会回到 Grain 调度器上。

<a id="interleaving-and-reentrancy"></a>

## 交错执行与可重入性

交错执行允许 Orleans 在先前的请求尚未完成时开始或恢复另一个请求。激活实例仍以单线程方式执行，但不同请求的执行轮次可以交替进行。每次 `await` 之后，都要重新检查关于可变状态的假设是否仍然成立。

| 机制 | 作用范围 |
|---|---|
| <xref:Orleans.Concurrency.ReentrantAttribute> | 对该 Grain 类的所有请求均可交错执行。 |
| <xref:Orleans.Concurrency.AlwaysInterleaveAttribute> | 标记的接口方法可与任何请求交错执行。 |
| <xref:Orleans.Concurrency.ReadOnlyAttribute> | 标记的只读方法可与其他只读方法交错执行。 |
| <xref:Orleans.Concurrency.MayInterleaveAttribute> | 使用谓词检查每个请求。 |
| <xref:Orleans.Runtime.RequestContext.AllowCallChainReentrancy> | 在指定的调用链作用域中，可以回调该调用链中已有的激活实例。 |

选择能够解决问题且作用范围最小的机制。可重入性可以提高 I/O 密集型 Grain 的吞吐量，并防止循环调用导致的死锁，但也允许状态在不同执行轮次之间发生变化。

<a id="reentrant-grains"></a>

### 可重入 Grain

:::code language="csharp" source="../snippets/compiled/Grains/RequestsAndVersioningSnippets.cs" id="reentrant_catalog_grain":::
不同请求中的代码不会同时执行，但多个尚未完成的调用可以通过交替执行各自的轮次取得进展。

<a id="method-level-interleaving"></a>

### 方法级交错执行

:::code language="csharp" source="../snippets/compiled/Grains/RequestsAndVersioningSnippets.cs" id="interleaving_method_attributes":::
`ReadOnly` 是对调度器作出的承诺。正在处理的只读请求可以彼此交错执行，并共同阻止可写请求执行，直到所有这些只读请求完成，除非其他交错执行策略允许重叠。只读方法不得修改 Grain 状态。

<a id="predicate-based-interleaving"></a>

### 基于谓词的交错执行

`MayInterleave` 指定一个接收 <xref:Orleans.Serialization.Invocation.IInvokable> 的谓词。请使用其访问器方法；它没有 `Arguments` 属性：

:::code language="csharp" source="../snippets/compiled/Grains/RequestsAndVersioningSnippets.cs" id="may_interleave_grain":::
谓词应具有确定性、执行迅速且没有副作用。

<a id="call-chain-reentrancy"></a>

### 调用链可重入性

当已知的调用路径必须回调发起调用的激活实例时，可以使用调用链可重入性：

:::code language="csharp" source="../snippets/compiled/Grains/RequestsAndVersioningSnippets.cs" id="call_chain_reentrancy":::
该作用域在被释放之前，允许与这条调用链关联的回调。它的作用范围比将整个 Grain 标记为可重入更小。

<a id="timers-and-external-work"></a>

## 定时器与外部工作

Grain 定时器回调也参与请求调度。回调默认不与其他请求交错执行；确有需要时，显式配置 `GrainTimerCreationOptions.Interleave`。

要从其他线程提交工作并使其遵循激活实例的调度规则，请参阅[将后台工作作为 Grain 请求执行](#run-background-work-as-a-grain-request)。

仅在需要将不可避免的同步阻塞操作或 CPU 密集型工作与 Orleans 调度器隔离时，才使用 <xref:System.Threading.Tasks.Task.Run*>。不要在线程池委托中访问 Grain 状态。等待该任务，并在执行恢复到激活实例的调度器上之后再更新 Grain 状态。请参阅[外部任务与 Grain](external-tasks-and-grains.md)。

<a id="run-background-work-as-a-grain-request"></a>

## 将后台工作作为 Grain 请求执行

<xref:Orleans.GrainBaseExtensions.RunRequestAsync*> 在激活实例上将回调作为一个请求执行。可以从任意线程调用它，例如从流、消息传递或库的回调中调用，使结果在与 Grain 调用相同的调度规则下应用到 Grain 状态：

:::code language="csharp" source="../snippets/compiled/Grains/RequestsAndVersioningSnippets.cs" id="run_request_from_callback":::
使用默认的 <xref:Orleans.Runtime.LocalRequestOptions> 时，该请求具有以下行为：

- 遵循 Grain 的可重入规则；对于不可重入的 Grain，请求会在当前请求之后排队。
- 经过传入 Grain 调用筛选器；筛选器将其识别为没有参数的 `IGrainRequestInvoker.RunRequestAsync` 调用。
- 启动新的调用链。调用线程的 <xref:Orleans.Runtime.RequestContext> 值不会传递到回调中。
- 重新开始计算激活实例的空闲回收时间。
- 不受 <xref:Orleans.Configuration.SiloMessagingOptions.MaxEnqueuedRequestsHardLimit> 限制。

回调可以像本例一样是同步的，也可以返回 `Task` 或 `Task<TResult>`。返回的任务会以回调的结果或异常完成。回调会收到传给 `RunRequestAsync` 的取消令牌；如果请求仍在队列中时取消该令牌，请求也会被取消。如果激活实例在请求执行之前停用，该任务将因 <xref:Orleans.Runtime.OrleansMessageRejectionException> 而失败。

通过 `state` 参数传入 Grain 和其他值，并使用 `static` Lambda 表达式，这样调用就不必分配闭包对象。对于不可重入的 Grain，如果在同一激活实例的另一个请求中等待返回的任务，将会发生死锁，因为排队的请求必须等待当前请求完成。

<a id="configure-the-request"></a>

### 配置请求

传入 <xref:Orleans.Runtime.LocalRequestOptions> 可以更改请求的调度方式。对于频繁执行但不应让原本空闲的激活实例一直驻留内存的后台工作，将 <xref:Orleans.Runtime.LocalRequestOptions.KeepAlive> 设为 `false`。将 <xref:Orleans.Runtime.LocalRequestOptions.Interleave> 设为 `true`，可使该请求像 `[AlwaysInterleave]` 方法一样，在其他请求的执行轮次之间交错执行：

:::code language="csharp" source="../snippets/compiled/Grains/RequestsAndVersioningSnippets.cs" id="run_request_options":::
<a id="deliver-a-custom-request"></a>

### 投递自定义请求

<xref:Orleans.Runtime.IGrainContext.ReceiveLocalRequest*> 将 <xref:Orleans.Runtime.ILocalRequest> 投递到激活实例，并采用与 `RunRequestAsync` 相同的调度规则。调用筛选器、日志和跟踪能够获取该请求自己的接口名称、方法名称和参数，因此自定义请求可以向它们描述实际工作。激活实例将请求的响应传给 <xref:Orleans.Runtime.ILocalRequest.Complete*>；如果拒绝该请求，则传递异常。请求将结果（包括失败）报告给其观察者：

:::code language="csharp" source="../snippets/compiled/Grains/RequestsAndVersioningSnippets.cs" id="receive_local_request":::
