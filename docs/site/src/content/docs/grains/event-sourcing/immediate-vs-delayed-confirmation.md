---
title: "立即和延迟事件确认"
description: "选择确认和排程 JournaledGrain的语义。"
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="immediate-and-delayed-event-confirmation"></a>

# 立即和延迟事件确认

<xref:Orleans.EventSourcing.JournaledGrain`2.RaiseEvent*> 更新临时视图并开始提交。 确认确定事件加入耐久性时, 命令日志 。

<a id="immediate-confirmation"></a>

## 立即确认

等待 <xref:Orleans.EventSourcing.JournaledGrain`2.ConfirmEvents*> 返回之前的 grain 方法承诺一个确认的结果:

:::code language="csharp" source="../../snippets/compiled/EventSourcing/EventSourcingSnippets.cs" id="immediate_confirmation":::

无法再次或互传电话,这又使另一个人无法观察 grain 在提交和确认之间。

平衡是可用性和延迟:响应等待选定的供应商和后援商店。连接问题可以在协议重试时维持确认。

<a id="delayed-confirmation"></a>

## 延迟确认

grain 可以在不等待 <xref:Orleans.EventSourcing.JournaledGrain`2.ConfirmEvents*>的情况下返回,或在确认待定时允许中断。这可以改进输送量,但改变方法保证的内容。

使用 :

- 确认视图的<xref:Orleans.EventSourcing.JournaledGrain`2.State> 。
- 证实事件加上当地未经证实的事件<xref:Orleans.EventSourcing.JournaledGrain`2.TentativeState> 。
- <xref:Orleans.EventSourcing.JournaledGrain`2.UnconfirmedEvents> 等待的后缀。

暂时状态不是一个持久的许诺。 激活在确认前可能会失败, 而竞相更新可以改变最终订单或拒绝有条件事件。

<a id="orleans-scheduling-still-applies"></a>

## Orleans 时间安排仍然适用

Reentrancy 在不同线索上不会同时运行两个 grain 。 状态可以跨 `await` 进行改变, 因为第一个转弯可以在暂停时运行。 截取值需要横跨 `await` 或随后重新检查状态 。

选择并记录每个 grain 方法的语义法:确认返回前或接受后地确认。 调用者不必从执行细节中推断耐久性。
