---
title: JournaledGrain 实例与冲突
description: 了解 JournaledGrain 实例的乐观并发控制和同步机制。
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="journaled-grain-instances-and-conflicts"></a>
# 日志化 Grain 实例与冲突

高级 Orleans 部署可能存在表示同一个逻辑日志的多个实例。日志一致性提供程序围绕同一个已确认事件序列协调这些实例。

在给定的已确认版本上，各实例从同一个事件序列派生相同的状态。提交尚未确认时，本地暂定视图可能不同。

<a id="racing-updates"></a>
## 竞争更新

无条件事件最终由提供程序排序。事件最终得到确认时，其在序列中的位置可能比暂定视图预期的更靠后。因此，状态转换逻辑必须对任何被接受的事件顺序都有效。

当有效性取决于当前观察到的版本时，使用 <xref:Orleans.EventSourcing.JournaledGrain`2.RaiseConditionalEvent*>：

:::code language="csharp" source="../../snippets/compiled/EventSourcing/EventSourcingSnippets.cs" id="conditional_update":::

提供程序将预期的已确认版本与存储中的版本进行比较。如果其他更新推进了日志版本，则不会追加该事件，并返回 `false`。随后 Grain 可以根据刷新后的状态重新评估命令。

<a id="explicit-synchronization"></a>
## 显式同步

调用 <xref:Orleans.EventSourcing.JournaledGrain`2.RefreshNow*>，以确认本地提交并从全局日志刷新状态：

:::code language="csharp" source="../../snippets/compiled/EventSourcing/EventSourcingSnippets.cs" id="refresh_replicated_instance":::

在作出需要最新已确认视图的决策之前，这很有用。该操作会产生存储或协议开销，并可能在后端服务不可用时等待。

<a id="deployment-boundary"></a>
## 部署边界

该协议包含感知集群的通知及并发机制，但本身不会提供地理复制的应用程序。多集群部署必须提供：

- 兼容的 Orleans 多集群配置及连接能力。
- 每个参与实例均可访问且满足所需一致性要求的存储。
- 行为适合该拓扑的提供程序。
- 关于写入区域、故障转移、延迟及冲突处理的应用程序级决策。

自定义提供程序注册时的 `primaryCluster` 参数不会限制提交。不要依靠该参数实现写入区域限制、复制、访问控制或故障转移。

有关协议模型及其设计背景，请参阅 Microsoft Research 的论文 [Geo-Distribution of Actor-Based Services](https://www.microsoft.com/research/publication/geo-distribution-actor-based-services/) 和 [Global Sequence Protocol](https://www.microsoft.com/research/publication/global-sequence-protocol-a-robust-abstraction-for-replicated-shared-state-extended-version/)。
