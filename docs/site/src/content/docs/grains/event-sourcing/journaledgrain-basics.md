---
title: JournaledGrain API
description: 使用 JournaledGrain 定义状态转换并确认事件。
ms.date: 08/08/2026
ms.topic: concept-article
---

<a id="the-journaled-grain-api"></a>
# 日志化 Grain API

使采用事件溯源的 Grain 继承 <xref:Orleans.EventSourcing.JournaledGrain`2>：

:::code language="csharp" source="../../snippets/compiled/EventSourcing/EventSourcingSnippets.cs" id="journaled_grain":::

`TGrainState` 必须是具有公共无参构造函数的类。`TEventBase` 是该 Grain 的事件所共有的类或接口。状态类型及事件类型必须可序列化，因为提供程序可能需要持久化或发送它们。

只有一个类型参数的 <xref:Orleans.EventSourcing.JournaledGrain`1> 形式使用 `object` 作为事件基类型。

<a id="confirmed-and-tentative-state"></a>
## 已确认状态与暂定状态

- <xref:Orleans.EventSourcing.JournaledGrain`2.State> 仅包含已确认事件产生的状态。
- <xref:Orleans.EventSourcing.JournaledGrain`2.Version> 是已确认事件的数量。
- <xref:Orleans.EventSourcing.JournaledGrain`2.TentativeState> 还包含本地提交但尚未确认的事件产生的状态。
- <xref:Orleans.EventSourcing.JournaledGrain`2.UnconfirmedEvents> 返回当前未确认的尾部事件序列。

不要直接修改 <xref:Orleans.EventSourcing.JournaledGrain`2.State> 或 <xref:Orleans.EventSourcing.JournaledGrain`2.TentativeState>。应通过引发事件来更改状态。

<a id="define-transitions"></a>
## 定义状态转换

默认情况下，Orleans 会在状态对象上动态调用最匹配的 `Apply` 重载：

:::code language="csharp" source="../../snippets/compiled/EventSourcing/EventSourcingSnippets.cs" id="event_sourced_state":::

也可以重写 <xref:Orleans.EventSourcing.JournaledGrain`2.TransitionState*>。状态转换逻辑必须具有确定性，并且只能修改传入的状态。提供程序可能多次重放状态转换，因此不要在状态转换方法中执行 I/O 或产生其他副作用。

> [!CAUTION]
> 不要通过从 <xref:Orleans.EventSourcing.JournaledGrain`2.TransitionState*> 抛出异常来拒绝事件。事件溯源运行时调用的状态转换代码抛出异常时，内置提供程序会捕获并记录该异常，但不会取消提交。如果存储接受了更新，即使状态转换未完成，<xref:Orleans.EventSourcing.JournaledGrain`2.ConfirmEvents*> 也可能完成，<xref:Orleans.EventSourcing.JournaledGrain`2.RaiseConditionalEvent*> 也可能返回 `true`，<xref:Orleans.EventSourcing.JournaledGrain`2.Version> 也可能递增。Orleans 不会回滚异常抛出前已经进行的修改，因此内存中的状态（以及使用状态存储时的持久化快照）可能包含部分完成的状态转换。应在引发事件前验证命令，并确保状态转换方法不抛出异常。

<a id="raise-and-confirm-events"></a>
## 引发并确认事件

<xref:Orleans.EventSourcing.JournaledGrain`2.RaiseEvent*> 提交事件，但不等待持久化确认：

:::code language="csharp" source="../../snippets/compiled/EventSourcing/EventSourcingSnippets.cs" id="raise_and_confirm":::

当 Grain 方法承诺其事件已经得到确认时，应在返回前等待 <xref:Orleans.EventSourcing.JournaledGrain`2.ConfirmEvents*> 完成。如果不等待确认，Orleans 会在后台继续确认，调用方可能观察到暂定行为。

使用 <xref:Orleans.EventSourcing.JournaledGrain`2.RaiseEvents*> 以原子方式提交一组相关事件：

:::code language="csharp" source="../../snippets/compiled/EventSourcing/EventSourcingSnippets.cs" id="raise_many_and_confirm":::

提供程序将该事件序列作为一次日志追加操作提交。已确认版本增加的数值等于事件数量。

<a id="conditional-events"></a>
## 条件事件

当事件仅针对当前观察到的版本有效时，使用 <xref:Orleans.EventSourcing.JournaledGrain`2.RaiseConditionalEvent*> 或 <xref:Orleans.EventSourcing.JournaledGrain`2.RaiseConditionalEvents*>：

:::code language="csharp" source="../../snippets/compiled/EventSourcing/EventSourcingSnippets.cs" id="raise_conditional_event":::

返回的任务在条件追加操作的结果确定后完成。`false` 表示另一个更新赢得了版本竞争，该事件未被追加。应根据刷新后的状态重新评估命令，不要将冲突视为成功。

<a id="synchronize-and-retrieve-events"></a>
## 同步并检索事件

<xref:Orleans.EventSourcing.JournaledGrain`2.RefreshNow*> 确认已提交的事件，并从存储刷新视图：

:::code language="csharp" source="../../snippets/compiled/EventSourcing/EventSourcingSnippets.cs" id="refresh_now":::

只有在提供程序保留并公开已确认事件时，<xref:Orleans.EventSourcing.JournaledGrain`2.RetrieveConfirmedEvents*> 才能返回已确认事件序列中的一段。状态存储和自定义存储不会通过此 API 公开事件，而日志存储支持此功能。

只有提供程序支持时，<xref:Orleans.EventSourcing.JournaledGrain`2.ClearLogAsync*> 才会重置状态并丢弃已确认及未确认事件。清除日志是破坏性操作，而不是架构迁移机制。

<a id="evolve-state-and-events"></a>
## 状态与事件的演进

对于基于重放的存储，历史事件仍是持久化契约的一部分。应确保其序列化形式仍可读取并保留状态转换行为，或在自定义存储中引入显式的版本升级转换或迁移。对于快照存储，则需要保证已存储的状态快照仍可读取。使用具有生产环境数据特征的历史数据测试激活与重放。
