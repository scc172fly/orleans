---
title: 取消 Orleans Grain 调用
description: 使用 CancellationToken 对 Orleans Grain 调用进行协作式取消。
ms.date: 08/07/2026
ms.topic: concept-article
---

<a id="cancel-orleans-grain-calls"></a>
# 取消 Orleans Grain 调用

Orleans 支持在 Grain 方法中使用 <xref:System.Threading.CancellationToken> 参数。取消是协作式的：Orleans 投递取消信号，由 Grain 的实现决定在何处可以安全停止执行。

<a id="add-cancellation-to-a-contract"></a>
## 为接口契约添加取消支持

最多添加一个 <xref:System.Threading.CancellationToken> 参数。将其放在参数列表末尾；如果调用方通常不需要取消操作，则将其声明为可选参数：

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="cancellable_grain_interface":::

在实现中观察令牌的取消状态，并将令牌传递给支持取消的 API：

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="observe_cancellation":::

如果发送调用前令牌已经取消，Orleans 不会发出请求。如果取消信号在请求排队期间到达，Orleans 可以在执行前将请求移出队列。一旦开始执行，Grain 方法就必须自行观察令牌的取消状态。

<a id="cancel-a-call"></a>
## 取消调用

传入由 <xref:System.Threading.CancellationTokenSource> 提供的令牌：

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="cancel_grain_call":::

取消信号可以跨客户端到 Grain 以及 Grain 到 Grain 的调用传播。网络发生故障时，只能尽力投递取消信号；取消也不会回滚 Grain 观察到令牌取消前已经完成的副作用。

<a id="timeouts-and-cancellation"></a>
## 超时与取消

响应超时与取消解决的是不同的问题：

- **超时**：未及时收到响应时，调用方停止等待。
- **取消**：请求被调用方协作式地停止执行。

默认情况下，超时的调用不会被取消。<xref:Orleans.Configuration.MessagingOptions.CancelRequestOnTimeout> 默认为 `false`。如果希望超时操作收到取消信号，需要显式启用：

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="cancel_request_on_timeout":::

即使启用该选项，超时也无法证明操作已经停止。取消消息可能延迟，方法可能没有观察其令牌，或者副作用可能已经完成。

<xref:Orleans.Configuration.MessagingOptions.WaitForCancellationAcknowledgement> 同样默认为 `false`。启用后，调用方会等待被调用方的取消确认，而不是立即完成本地取消。仅当这种更强的协调机制值得额外的延迟和消息开销时才启用。

<a id="design-cancellable-operations"></a>
## 设计可取消的操作

- 在执行开销较大的工作之前，以及各自可安全停止的工作单元之间，检查令牌。
- 将令牌传递给 I/O 操作及其他异步依赖项。
- 在每个取消点都保证状态一致。
- 完成清理后重新抛出 <xref:System.OperationCanceledException>，使调用方能够区分取消和故障。
- 如果调用方可能在结果不确定时重试，应使对外可见的操作具有幂等性。

从 Grain 代码注册的取消回调在该 Grain 的调度上下文中执行。保持回调简短，避免阻塞。

<a id="contract-evolution"></a>
## 契约演进

Orleans 在生成请求契约时对 <xref:System.Threading.CancellationToken> 进行特殊处理。添加或移除令牌参数，与针对另一种接口形式编译的调用方在通信协议上兼容：缺失的令牌表示为 <xref:System.Threading.CancellationToken.None?displayProperty=nameWithType>，旧版调用方额外传入的令牌则可以忽略。将新增参数声明为可选参数，也可使常见调用点保持 C# 源代码兼容性。

旧版 <xref:Orleans.GrainCancellationToken> API 已过时。请改用 <xref:System.Threading.CancellationToken> 和 <xref:System.Threading.CancellationTokenSource>。

<a id="related-net-guidance"></a>
## 相关 .NET 指南

- [托管线程中的取消](https://learn.microsoft.com/dotnet/standard/threading/cancellation-in-managed-threads)
- [任务取消](https://learn.microsoft.com/dotnet/standard/parallel-programming/task-cancellation)
