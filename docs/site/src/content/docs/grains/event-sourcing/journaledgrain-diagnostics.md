---
title: JournaledGrain 诊断
description: 监控日志一致性连接问题及统计数据。
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="journaled-grain-diagnostics"></a>
# 日志化 Grain 诊断

<a id="connection-issues"></a>
## 连接问题

重写这些回调以观察持久化或协议故障及其恢复情况：

:::code language="csharp" source="../../snippets/compiled/EventSourcing/EventSourcingSnippets.cs" id="connection_issue_callbacks":::

<xref:Orleans.EventSourcing.Common.PrimaryOperationFailed> 表示访问提供程序主存储时发生的失败。具体问题类型包括读取或更新状态存储、日志存储或自定义存储失败。同一类别的问题重复发生时，会产生多次问题回调；问题解决时会产生一次相应的已解决回调。

这些回调是诊断手段，不能替代对失败或延迟的 Grain 调用的观察。避免从回调抛出异常；Orleans 会捕获并记录回调异常。

<a id="per-grain-statistics"></a>
## 单个 Grain 的统计数据

使用 <xref:Orleans.EventSourcing.JournaledGrain`2.EnableStatsCollection*> 启用统计数据收集，使用 <xref:Orleans.EventSourcing.JournaledGrain`2.GetStats*> 获取可为 null 的 <xref:Orleans.EventSourcing.LogConsistencyStatistics> 值，并使用 <xref:Orleans.EventSourcing.JournaledGrain`2.DisableStatsCollection*> 停止收集。

统计数据收集需要显式启用，且仅针对当前日志化 Grain。应将其用于有针对性的诊断，而不是作为生产环境唯一的遥测系统。将其与提供程序指标、存储服务指标、Orleans 日志以及调用延迟和错误遥测相结合。
