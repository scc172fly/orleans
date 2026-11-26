---
title: "JournaledGrain 国家通知"
description: "重新对确认的状态和临时 JournaledGrain 状态变化进行响应。"
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="journaled-grain-state-notifications"></a>

# 日记月刊 grain 国家通知

覆盖 <xref:Orleans.EventSourcing.JournaledGrain`2.OnStateChanged*> 在确认版本增加后作出反应:

:::code language="csharp" source="../../snippets/compiled/EventSourcing/EventSourcingSnippets.cs" id="confirmed_state_changed":::

它可以在装上更新的国家、确认本地事件或收到协议通知后运行。 它不是一而再一次整合- 活动回调。 除非应用程序添加了自己的应急能力或发件箱协议, 否则它不会在这里产生外部副作用 。

当临时视图更改时覆盖 <xref:Orleans.EventSourcing.JournaledGrain`2.OnTentativeStateChanged*> 以作出反应:

:::code language="csharp" source="../../snippets/compiled/EventSourcing/EventSourcingSnippets.cs" id="tentative_state_changed":::

<xref:Orleans.EventSourcing.JournaledGrain`2.RaiseEvent*> 触发此回调。 当确认或同步改变临时后缀时, 它也可以运行 。

两次回调都按照 Orleans 转折列表执行。 它们应该很快, 不能假设它们观察到的状态在以后一段时间里没有变化 `await`。
