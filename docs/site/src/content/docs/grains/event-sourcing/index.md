---
title: 事件溯源概述
description: 使用 JournaledGrain 和日志一致性提供程序构建采用事件溯源的 Orleans Grain。
ms.date: 08/02/2026
ms.topic: overview
---

<a id="event-sourcing-overview"></a>
# 事件溯源概述

Orleans 事件溯源使用受支持的 `Microsoft.Orleans.EventSourcing` 包中的 <xref:Orleans.EventSourcing.JournaledGrain`2>。日志化 Grain 将变更表示为事件，并按顺序应用这些事件以派生状态。

<xref:Orleans.EventSourcing.JournaledGrain`2> 将以下内容分离：

- **状态**：用于响应请求的聚合视图。
- **事件**：Grain 提交的不可变更改。
- **日志一致性**：负责对事件排序、确认、持久化及刷新的协议。
- **存储**：由配置的日志一致性提供程序选择。

内置提供程序支持快照存储、将完整事件序列存储为一条记录，以及应用程序定义的存储。它们的可扩展性和检索能力各不相同，请参考[提供程序对比](log-consistency-providers.md)选择适合的提供程序。

<a id="consistency-model"></a>
## 一致性模型

已确认的 <xref:Orleans.EventSourcing.JournaledGrain`2.Version> 等于已确认事件的数量。给定版本的已确认状态由同一个有序事件序列派生。在本地引发的事件得到确认之前，也可能对 <xref:Orleans.EventSourcing.JournaledGrain`2.TentativeState> 产生影响。

日志一致性提供程序使用乐观并发控制和协议通知，协调可能存在于高级部署拓扑中的多个实例。这并非自动地理复制：Orleans 不会预配复制存储、部署多个集群、在不同区域之间路由用户，也不会定义灾难恢复策略。任何多集群设计都必须另外提供共享或可访问的存储及 Orleans 多集群连接，并针对所选提供程序进行验证。

<a id="articles"></a>
## 相关文章

- [JournaledGrain 基础](journaledgrain-basics.md)
- [复制实例与冲突](replicated-instances.md)
- [立即确认与延迟确认](immediate-vs-delayed-confirmation.md)
- [通知](notifications.md)
- [事件溯源配置](event-sourcing-configuration.md)
- [日志一致性提供程序](log-consistency-providers.md)
- [JournaledGrain 诊断](journaledgrain-diagnostics.md)

<a id="event-sourcing-and-experimental-journaling"></a>
## 事件溯源与实验性的 Journaling

`Microsoft.Orleans.EventSourcing` 与 `Microsoft.Orleans.Durable` 的 Journaling 功能是不同的包及编程模型。

- 事件溯源使用 `JournaledGrain<TState, TEvent>` 和日志一致性提供程序。
- [Orleans Journaling](../journaling/index.md) 使用 <xref:Orleans.Durable.Journaling.IDurableStateManager> 管理持久化值及集合，并提供 <xref:Orleans.Durable.Journaling.DurableGrain> 作为便捷基类。

Journaling 随处于 alpha 阶段的 `Microsoft.Orleans.Durable` 包发布。它的编程模型面向正在评估面向可变持久化状态的基于操作的持久化方案的应用程序。事件溯源则适用于将领域变更建模为事件并使用日志一致性提供程序的应用程序。
