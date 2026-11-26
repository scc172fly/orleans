---
title: "下 JournaledGrain API"
description: "使用 JournaledGrain定义状态过渡和确认事件。"
ms.date: 08/08/2026
ms.topic: concept-article
---

<a id="the-journaled-grain-api"></a>

# 日记 grain API

产生事件源 grain 调自 <xref:Orleans.EventSourcing.JournaledGrain`2>:

:::code language="csharp" source="../../snippets/compiled/EventSourcing/EventSourcingSnippets.cs" id="journaled_grain":::

`TGrainState` 必须是一个具有公共无参数构建器的类。 `TEventBase` 是 grain事件的共同类或界面。 国家和事件类型必须可连号, 因为提供者可以持续或发送它们 。

单参数 <xref:Orleans.EventSourcing.JournaledGrain`1> 表用 `object` 作为事件基类型。

<a id="confirmed-and-tentative-state"></a>

## 确认和暂定状态

- <xref:Orleans.EventSourcing.JournaledGrain`2.State> 仅包含经确认的事件。
- <xref:Orleans.EventSourcing.JournaledGrain`2.Version> 是确认的事件数目。
- <xref:Orleans.EventSourcing.JournaledGrain`2.TentativeState> 还包括当地提交的未经证实的事件。
- <xref:Orleans.EventSourcing.JournaledGrain`2.UnconfirmedEvents> 返回当前未经证实的后缀 。

不要直接变换 <xref:Orleans.EventSourcing.JournaledGrain`2.State> 或 <xref:Orleans.EventSourcing.JournaledGrain`2.TentativeState> 。通过提醒事件改变状态 。

<a id="define-transitions"></a>

## 定义过渡

默认情况下, Orleans 会动态地援引国家上最近的 `Apply` 超载:

:::code language="csharp" source="../../snippets/compiled/EventSourcing/EventSourcingSnippets.cs" id="event_sourced_state":::

或者,取代 <xref:Orleans.EventSourcing.JournaledGrain`2.TransitionState*>。过渡逻辑必须是决定性的,并且只能改变提供状态。提供者可以重复多次过渡,所以不要执行I/O或过渡方法的其他副作用。

> [!CAUTION]
> 不要丢弃 <xref:Orleans.EventSourcing.JournaledGrain`2.TransitionState*> 拒绝事件。 当“事件自动递增运行时间” 所引用的过渡代码丢弃时, 内建提供者会抓取并记录例外; 它不会取消提交文件。 如果存储接受更新, <xref:Orleans.EventSourcing.JournaledGrain`2.ConfirmEvents*> 可完成, <xref:Orleans.EventSourcing.JournaledGrain`2.RaiseConditionalEvent*> 可以返回 `true`,和 <xref:Orleans.EventSourcing.JournaledGrain`2.Version> 即使过渡尚未完成,也可以向前推进。 Orleans 无法反弹之前发生的突变,所以在状态中,随着国家储存的状态,持续着的快照可以包含部分过渡。 在提出事件前验证命令并保持过渡方法不被扔掉。

<a id="raise-and-confirm-events"></a>

## 提出并确认事件

<xref:Orleans.EventSourcing.JournaledGrain`2.RaiseEvent*> 提交事件,但不必等待确认

:::code language="csharp" source="../../snippets/compiled/EventSourcing/EventSourcingSnippets.cs" id="raise_and_confirm":::

等待 <xref:Orleans.EventSourcing.JournaledGrain`2.ConfirmEvents*> 返回之前的 grain 保证它的事件得到证实。 Orleans 继续确认背景,打电话者可以观察暂时行为。

以 <xref:Orleans.EventSourcing.JournaledGrain`2.RaiseEvents*>原子提交相关序列:

:::code language="csharp" source="../../snippets/compiled/EventSourcing/EventSourcingSnippets.cs" id="raise_many_and_confirm":::

供应商将序列作为一个日志附件提交。经确认的版本按事件次数提前提交。

<a id="conditional-events"></a>

## 有条件事件

使用 <xref:Orleans.EventSourcing.JournaledGrain`2.RaiseConditionalEvent*> 或 <xref:Orleans.EventSourcing.JournaledGrain`2.RaiseConditionalEvents*> , 当事件仅对当前所观察到的版本有效时:

:::code language="csharp" source="../../snippets/compiled/EventSourcing/EventSourcingSnippets.cs" id="raise_conditional_event":::

返回任务在有条件附加件解析后完成。 `false` 意指再次更新中标版本竞赛,事件没有被附加。使用刷新状态重新评价命令; 不要将冲突视为成功 。

<a id="synchronize-and-retrieve-events"></a>

## 同步和检索事件

<xref:Orleans.EventSourcing.JournaledGrain`2.RefreshNow*> 确认已提交的事件,并从存储处刷新视图:

:::code language="csharp" source="../../snippets/compiled/EventSourcing/EventSourcingSnippets.cs" id="refresh_now":::

<xref:Orleans.EventSourcing.JournaledGrain`2.RetrieveConfirmedEvents*> 只有在提供商保留并披露时才会返回确认的段。国家储存和自定义储存不会通过此 API暴露事件;日志储存是这样做的。

<xref:Orleans.EventSourcing.JournaledGrain`2.ClearLogAsync*> 重新发布状态和抛弃物, 只有在提供商支持的情况下才能确认和证实事件。 清除日志是破坏性的, 不是一种计划迁移机制 。

<a id="evolve-state-and-events"></a>

## 动态状态和事件

用于重播存储的,历史事件仍然是耐久合同的一部分。保持其序列式可读化形状并保存过渡行为,或在自定义存储中引入清晰的上传/迁移。快照存储要求存储状态快照仍可以读取。同时使用生产型历史数据测试激活和重放。
