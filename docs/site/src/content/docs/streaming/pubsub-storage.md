---
title: "配置 PubSub 存储"
description: "持久性 Orleans 使用 PubSubStore grain 储存提供者。"
ms.date: 08/02/2026
ms.topic: how-to
---

<a id="configure-pubsub-storage"></a>

# 配置 PubSub 存储

Orleans 流源供应商使用酒吧/分会合连接生产者和消费者。 grain 存储供应商的名称 `PubSubStore` 储存了 grain-基于酒吧/分会执行的明确的订阅元数据。

是否保持`PubSubStore` 的耐久性决定了明确订阅记录是否在群组失去记忆状态后继续存在:

- <xref:Orleans.Hosting.MemoryGrainStorageSiloBuilderExtensions.AddMemoryGrainStorage*> 配置时使用名称 `PubSubStore` 适合开发和测试。当集群状态丢失时,记录就会丢失。
- 耐久 grain 存储供应商保存整个 silo 和集群重新启动的记录,但以该供应商本身的可用性和一致性为条件。
- 暗含订阅来自 grain 元数据,而不是作为明确的订阅记录创建。

即便有耐久 `PubSubStore`,明确的消费者在激活后必须调用 <xref:Orleans.Streams.StreamSubscriptionHandle`1.ResumeAsync*> 附加其当前观察实例。相反,持久事件存储并不能使订阅记录具有耐久性。根据回收要求配置两个层次。

<a id="azure-table-storage-example"></a>

## Azure 表格存储示例

首选 <xref:Orleans.Hosting.AzureTableSiloBuilderExtensions.AddAzureTableGrainStorage*> 的被管理身份 :

:::code language="csharp" source="snippets/streaming/Configuration.cs" id="pubsub_managed_identity":::

也支持连接字符串:

:::code language="csharp" source="snippets/streaming/Configuration.cs" id="pubsub_connection_string":::

使用稳定的 Orleans 服务代号,并在集群重新启动时使用相同的耐久存储配置。更改服务身份或删除支持表格将创建符合逻辑的新订阅登记簿。

<a id="operational-guidance"></a>

## 业务指导

- 备份并像其它应用程序元数据一样监视 `PubSubStore` 。
- 保持提供者名称稳定。 提供者重命名会从酒吧/ 子系统的角度更改流身份 。
- 在不再需要时删除订阅, 以 <xref:Orleans.Streams.StreamSubscriptionHandle`1.UnsubscribeAsync*> 方式删除订阅。
- 在替换 `PubSubStore`之前,先计划如何重新创建现有的明确订阅。

内部会合和拉动剂设计见 [Orleans 流执行](../implementation/streams-implementation/index.md)。
