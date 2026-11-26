---
title: "JournaledGrain 诊断"
description: "监测对齐连接问题和统计。"
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="journaled-grain-diagnostics"></a>

# 日报 grain 诊断

<a id="connection-issues"></a>

## 连接问题

克服这些回调,观察持久性或协议故障及其恢复:

:::code language="csharp" source="../../snippets/compiled/EventSourcing/EventSourcingSnippets.cs" id="connection_issue_callbacks":::

<xref:Orleans.EventSourcing.Common.PrimaryOperationFailed> identifies failed access to the provider's primary storage. Concrete issue types include failures to read or update state storage, log storage, or custom storage. Repeated failures in one category produce repeated issue callbacks; resolution produces one corresponding resolved callback.

这些回调是诊断,不是用来替代观察失败或延迟 grain 呼叫的。避免从中丢弃; Orleans 捕获和日志回调例外。

<a id="per-grain-statistics"></a>

## 人均grain 统计

启用收藏 <xref:Orleans.EventSourcing.JournaledGrain`2.EnableStatsCollection*>,获取一个无效 <xref:Orleans.EventSourcing.LogConsistencyStatistics> 数值的值 <xref:Orleans.EventSourcing.JournaledGrain`2.GetStats*>,停止收集 <xref:Orleans.EventSourcing.JournaledGrain`2.DisableStatsCollection*>.

收藏是选择输入的, 本地可访问日记 grain。 使用它进行重点分析, 而不是作为唯一的生产遥测系统。 将其与供应商量度、 储存服务量度、 Orleans 日志结合起来, 并调用长期/ 高级遥测 。
