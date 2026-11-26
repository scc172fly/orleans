---
title: "JournaledGrain 情况与冲突"
description: "理解 JournaledGrain 实例的乐观同值和同步。"
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="journaled-grain-instances-and-conflicts"></a>

# 日报 grain 事件和冲突

高级 Orleans 部署可能遇到一个以上的例子, 代表同一逻辑日记。 日志一致性提供者围绕一个经确认的事件序列协调这些事件。

在某一确认的版本中,同一事件序列产生相同状态的情况。 当地临时意见可能不同,而提交材料则未经证实。

<a id="racing-updates"></a>

## 正在计算更新的更新

无条件事件最终由提供商下令。 事件可以在时间顺序上比预想的预想稍晚确认, 因此过渡逻辑对于任何被接受的命令都必须有效。

当有效性取决于当前观察的版本时,请使用 <xref:Orleans.EventSourcing.JournaledGrain`2.RaiseConditionalEvent*>:

:::code language="csharp" source="../../snippets/compiled/EventSourcing/EventSourcingSnippets.cs" id="conditional_update":::

供应商比较了预期确认版本和存储。 如果再次更新提前了日志, 它不会附加事件并返回 `false`。 grain 然后可以根据更新状态重新评价命令 。

<a id="explicit-synchronization"></a>

## 明确的同步

呼叫 <xref:Orleans.EventSourcing.JournaledGrain`2.RefreshNow*> 确认本地提交信息,并从全球日志中刷新:

:::code language="csharp" source="../../snippets/compiled/EventSourcing/EventSourcingSnippets.cs" id="refresh_replicated_instance":::

这对于做出需要最新确认视图的决定是有用的。 它需要进行存储/议定书工作, 可以在没有支持服务时等待 。

<a id="deployment-boundary"></a>

## 部署边界

议定书载有了解集群的通知和同值货币机制,但本身并不提供地理上复制的应用。

- 兼容 Orleans 多组配置和连通。
- 储存每个参与机构都能够以必要的一致性达到的储存。
- 其行为符合地形的提供者
- 申请区域、故障、延迟和冲突处理。

用户服务供应商的 `primaryCluster` 注册论证并不限制提交文件。 不要把它当作一个写作区域、复制、访问控制或失败机制来依赖它。

议定书模型及其设计背景见 Microsoft 研究文件 [以行为体为基础的服务的地理分布](https://www.microsoft.com/research/publication/geo-distribution-actor-based-services/) 和 [全球序列议定书](https://www.microsoft.com/research/publication/global-sequence-protocol-a-robust-abstraction-for-replicated-shared-state-extended-version/)。
