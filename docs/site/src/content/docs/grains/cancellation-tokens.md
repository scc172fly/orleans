---
title: "取消 Orleans grain 调用"
description: "使用 CancellationToken 合作取消 Orleans grain 电话。"
ms.date: 08/07/2026
ms.topic: concept-article
---

<a id="cancel-orleans-grain-calls"></a>

# 取消 Orleans grain 调用

Orleans 支持 <xref:System.Threading.CancellationToken> 参数的参数 grain 取消是合作性的: Orleans 发送取消信号, grain 执行决定了停止安全的地方。

<a id="add-cancellation-to-a-contract"></a>

## 添加取消合同到合同

最多添加一个 <xref:System.Threading.CancellationToken> 参数。 将其放在最后, 当调用者通常不需要取消时, 让它可以选择 :

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="cancellable_grain_interface":::

执行时要注意该标记,

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="observe_cancellation":::

如果在发出此呼叫之前一个命令已被取消, Orleans 就不会发出请求。 如果取消已到达而请求被排队, Orleans 可以在执行前将其删除。 一旦执行开始, grain 方法必须遵守该标记 。

<a id="cancel-a-call"></a>

## 取消呼叫

从一个 <xref:System.Threading.CancellationTokenSource>传递一个标记 :

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="cancel_grain_call":::

取消可以跨客户端到grain 和 grain- 到grain 调用。 发送是网络故障下的最大努力, 取消不会使在 grain 观察标记之前完成的副作用倒转。

<a id="timeouts-and-cancellation"></a>

## 超时和取消

反应超时和取消回答不同的问题:

- A **超时** 等没有反应时,
- **取消** 要求被调用者停止合作。

默认情况下, 超时的调用不会取消。 <xref:Orleans.Configuration.MessagingOptions.CancelRequestOnTimeout> 默认为 `false`。 当超时操作接收取消信号时, 明确启用它 :

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="cancel_request_on_timeout":::

即使启用了, 超时并不能证明操作已停止。 取消信件可能会被延迟, 方法可能没有观察到它的迹象, 或者副作用可能已经完成 。

<xref:Orleans.Configuration.MessagingOptions.WaitForCancellationAcknowledgement> also defaults to `false`. Enabling it makes the caller wait for acknowledgement from the callee rather than completing local cancellation immediately. Use it only when that stronger coordination is worth the added latency and messaging.

<a id="design-cancellable-operations"></a>

## 可切换设计操作

- 贵重工作之前检查标牌,独立安全的工作单位之间核对。
- 将牌照传递给 I/ O 和其他非同步依赖关系 。
- 在每个取消点保持请假状态一致 。
- 清理后重新丢弃 <xref:System.OperationCanceledException> , 这样调用者可以区分取消和失败 。
- 当一个打电话者在结果不确定后可能重新尝试时,让外部可见的行动成为一等的。

登记取消回调 grain 代码执行 grain时间框架。 请保持回调短, 避免阻塞 。

<a id="contract-evolution"></a>

## 合同演变

Orleans 处理 <xref:System.Threading.CancellationToken> , 特别是在生成的请求合同中。 添加或删除一个象征性参数是与根据其它表格编译的调用器有线兼容的。 缺少的质物以 <xref:System.Threading.CancellationToken.None?displayProperty=nameWithType>表示, 旧调用器的额外代号可以忽略。 设置新添加的参数也可以保护通用调用站点的 C# 源兼容性 。

旧的 <xref:Orleans.GrainCancellationToken> API 已经过时。 请使用 <xref:System.Threading.CancellationToken> 和 <xref:System.Threading.CancellationTokenSource> 。

<a id="related-net-guidance"></a>

## 相关 .NET 指导

- [管理线索中注销](https://learn.microsoft.com/dotnet/standard/threading/cancellation-in-managed-threads)
- [取消任务取消](https://learn.microsoft.com/dotnet/standard/parallel-programming/task-cancellation)
