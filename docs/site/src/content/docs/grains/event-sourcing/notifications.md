---
title: JournaledGrain 状态通知
description: 响应 JournaledGrain 的已确认状态和暂定状态的变化。
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="journaled-grain-state-notifications"></a>
# 日志化 Grain 状态通知

重写 <xref:Orleans.EventSourcing.JournaledGrain`2.OnStateChanged*>，以在已确认版本可能增大之后作出响应：

:::code language="csharp" source="../../snippets/compiled/EventSourcing/EventSourcingSnippets.cs" id="confirmed_state_changed":::

此回调可能在加载较新状态、确认本地事件或收到协议通知后运行。它不是保证恰好执行一次的集成事件回调。除非应用程序添加了自己的幂等机制或 Outbox 协议，否则不要在此处执行外部副作用操作。

重写 <xref:Orleans.EventSourcing.JournaledGrain`2.OnTentativeStateChanged*>，以在暂定视图发生变化时作出响应：

:::code language="csharp" source="../../snippets/compiled/EventSourcing/EventSourcingSnippets.cs" id="tentative_state_changed":::

<xref:Orleans.EventSourcing.JournaledGrain`2.RaiseEvent*> 会触发此回调。当确认或同步改变暂定的尾部事件序列时，此回调也可能运行。

两个回调都在 Orleans 基于执行轮次的调度机制下执行。回调应尽快完成，并且不能假设其观察到的状态在后续 `await` 前后保持不变。
