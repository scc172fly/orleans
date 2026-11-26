---
title: "活动来源概览"
description: "构建事件源 Orleans grains 与 JournaledGrain 以及逻辑一致性提供者。"
ms.date: 08/02/2026
ms.topic: overview
---

<a id="event-sourcing-overview"></a>

# 活动来源概览

Orleans 事件 抛光用途 <xref:Orleans.EventSourcing.JournaledGrain`2> 支持的源代码 `Microsoft.Orleans.EventSourcing` 组合包。 grain 以事件形式变化,通过按顺序排列来得出其状态。

<xref:Orleans.EventSourcing.JournaledGrain`2> 分开:

- **国家**,用于答复请求的汇总观点。
- **事件**,即 grain提交的不可改变的更改。
- **日志一致性**,即命令、确认、维持和刷新活动的协议。
- **存储**,由配置的对数一致性提供者选择。

内建供应商支持快照存储、完整事件序列作为记录保存以及应用程序定义的存储。它们的可缩放性和检索能力各不相同,因此根据 [提供商比较](log-consistency-providers.md)选择一个供应商。

<a id="consistency-model"></a>

## 一致性模式

确认 <xref:Orleans.EventSourcing.JournaledGrain`2.Version> 确定事件的数量。在给定版本中,一个确认状态来自一个有顺序的事件序列。 <xref:Orleans.EventSourcing.JournaledGrain`2.TentativeState> 未经确认。

逻辑一致性提供者使用乐观的同值货币和协议通知来协调在高级部署地形中可能存在的情况。这不是自动地理复制: Orleans 不提供重复储存、部署多个集群、跨区域路线用户或界定灾后恢复政策。任何多组设计都必须单独提供共享/可达存储和 Orleans 多组连接,而且必须为选定的供应商验证。

<a id="articles"></a>

## 条款条款条款条款条款条款条条条条条条条条条条条条条

- [JournaledGrain 基本](journaledgrain-basics.md)
- [重复的事例和冲突](replicated-instances.md)
- [立即确认和延迟确认](immediate-vs-delayed-confirmation.md)
- [通知](notifications.md)
- [事件外包配置](event-sourcing-configuration.md)
- [逻辑一致性提供者](log-consistency-providers.md)
- [JournaledGrain 诊断](journaledgrain-diagnostics.md)

<a id="event-sourcing-and-experimental-journaling"></a>

## 活动 测试和实验性日刊

`Microsoft.Orleans.EventSourcing` 和 `Microsoft.Orleans.Durable` 的《日刊》是单独的成套资料和方案拟定模式。

- 事件抛光使用 `JournaledGrain<TState, TEvent>` 和日志一致性提供者。
- [Orleans 日刊](../journaling/index.md) 使用 <xref:Orleans.Durable.Journaling.IDurableStateManager> 作为耐久值和收藏, <xref:Orleans.Durable.Journaling.DurableGrain> 作为便利基级。

将船舶定时 `Microsoft.Orleans.Durable` 其方案拟订模式为评估基于操作的持久性以有利于可变持久状态的应用软件。
