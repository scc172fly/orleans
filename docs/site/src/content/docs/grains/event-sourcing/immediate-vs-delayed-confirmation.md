---
title: 立即确认与延迟确认事件
description: 为 JournaledGrain 选择确认语义和调度语义。
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="immediate-and-delayed-event-confirmation"></a>
# 立即确认与延迟确认事件

<xref:Orleans.EventSourcing.JournaledGrain`2.RaiseEvent*> 更新暂定视图并开始提交。确认决定事件何时加入持久化的有序日志。

<a id="immediate-confirmation"></a>
## 立即确认

当 Grain 方法承诺返回已确认的结果时，在返回前等待 <xref:Orleans.EventSourcing.JournaledGrain`2.ConfirmEvents*> 完成：

:::code language="csharp" source="../../snippets/compiled/EventSourcing/EventSourcingSnippets.cs" id="immediate_confirmation":::

还要等待条件事件任务完成。如果没有可重入或可交错的调用，这样可避免另一个执行轮次在提交与确认之间观察该 Grain 的状态。

其代价体现在可用性及延迟上：调用需要等待所选提供程序及后端存储。发生连接问题时，确认可能在协议重试期间一直等待。

<a id="delayed-confirmation"></a>
## 延迟确认

Grain 可以不等待 <xref:Orleans.EventSourcing.JournaledGrain`2.ConfirmEvents*> 就返回，也可以允许在等待确认期间交错执行。这可能提高吞吐量，但会改变方法所提供的保证。

使用：

- <xref:Orleans.EventSourcing.JournaledGrain`2.State> 获取已确认视图。
- <xref:Orleans.EventSourcing.JournaledGrain`2.TentativeState> 获取包含已确认事件及本地未确认事件的视图。
- <xref:Orleans.EventSourcing.JournaledGrain`2.UnconfirmedEvents> 获取待确认的尾部事件序列。

暂定状态并不是持久化保证。激活实例可能在确认之前发生故障，并发更新可能改变最终顺序，或导致条件事件被拒绝。

<a id="orleans-scheduling-still-applies"></a>
## Orleans 调度规则仍然适用

可重入性不会使两个 Grain 执行轮次在不同线程上同时执行。在 `await` 前后，状态可能发生变化，因为首个轮次暂停时另一个轮次可以运行。应捕获跨 `await` 所需的值，或在恢复执行后重新检查状态。

为每个 Grain 方法选择并记录一种语义：返回前完成确认，或者接受事件并在后台确认。调用方不应该从实现细节中推断持久化保证。
