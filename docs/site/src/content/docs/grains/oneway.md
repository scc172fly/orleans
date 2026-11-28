---
title: 单向 Grain 调用
description: 使用单向请求实现 Orleans 的尽力而为通知。
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="one-way-grain-calls"></a>
# 单向 Grain 调用

常规 Grain 调用返回一个任务，Orleans 收到方法响应时该任务完成。任务携带返回值或异常，告知调用方请求已经结束。

标记了 <xref:Orleans.Concurrency.OneWayAttribute> 的方法，在 Orleans 接受待发送的请求后即返回调用方。调用方不会收到执行完成的信号、结果或异常：

:::code language="csharp" source="../snippets/compiled/Grains/ServicesAndObserversSnippets.cs" id="one_way_audit":::
单向方法必须返回 <xref:System.Threading.Tasks.Task> 或 <xref:System.Threading.Tasks.ValueTask>，而非它们的泛型形式。

<a id="delivery-semantics"></a>
## 投递语义

单向调用采用尽力而为的投递方式：

- 返回的任务完成，并不意味着目标已接收或处理请求。
- 目标抛出的异常不会返回调用方。
- 调用方无法区分处理成功、消息丢失或目标发生故障。
- 由于没有响应，取消和响应超时语义无法提供有意义的完成保证。

仅对允许丢失且无需结果的通知使用单向调用。例如，遥测提示、缓存失效提示或冗余状态信号可能适合这种模式。

不要对需要确认的状态变更、金融操作、工作流状态转换或任何调用方可能需要重试的操作使用单向调用。如果投递和故障恢复很重要，应优先使用常规的请求-响应调用、持久化队列或 Orleans 流提供程序。

单向调用可以减少响应消息的开销，但应将其视为高级优化手段。在替换常规调用前，先进行性能测量。
