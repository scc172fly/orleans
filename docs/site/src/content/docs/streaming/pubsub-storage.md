---
title: 配置 PubSub 存储
description: 使用 PubSubStore Grain 存储提供程序持久化 Orleans 流的订阅元数据。
ms.date: 08/02/2026
ms.topic: how-to
---

<a id="configure-pubsub-storage"></a>

# 配置 PubSub 存储

Orleans 流提供程序通过发布/订阅会合机制连接生产者与消费者。名为 `PubSubStore` 的 Grain 存储提供程序保存基于 Grain 的发布/订阅实现所使用的显式订阅元数据。

`PubSubStore` 的持久性决定了显式订阅记录能否在集群内存状态丢失后继续存在：

- 调用 <xref:Orleans.Hosting.MemoryGrainStorageSiloBuilderExtensions.AddMemoryGrainStorage*> 并将名称配置为 `PubSubStore`，适用于开发和测试。集群状态丢失时，这些记录也会丢失。
- 持久化 Grain 存储提供程序能跨 Silo 和集群重启保留记录，具体取决于该提供程序自身的可用性与一致性保证。
- 隐式订阅从 Grain 元数据派生，而不是作为显式订阅记录创建。

即使使用持久化 `PubSubStore`，显式消费者仍必须在激活后调用 <xref:Orleans.Streams.StreamSubscriptionHandle`1.ResumeAsync*>，以附接当前观察者实例。反过来，事件存储具有持久性也不意味着订阅记录具有持久性。请根据恢复要求分别配置这两个层面。

<a id="azure-table-storage-example"></a>

## Azure Table Storage 示例

推荐使用托管标识与 <xref:Orleans.Hosting.AzureTableSiloBuilderExtensions.AddAzureTableGrainStorage*>：

:::code language="csharp" source="snippets/streaming/Configuration.cs" id="pubsub_managed_identity":::

也支持使用连接字符串：

:::code language="csharp" source="snippets/streaming/Configuration.cs" id="pubsub_connection_string":::

集群重启前后应使用稳定的 Orleans 服务 ID 以及相同的持久化存储配置。更改服务标识或删除底层表会创建一个逻辑上全新的订阅注册表。

<a id="operational-guidance"></a>

## 运维指南

- 像其他应用程序元数据一样备份和监控 `PubSubStore`。
- 保持提供程序名称稳定。从发布/订阅系统的角度看，重命名提供程序会改变流标识。
- 不再需要订阅时，请使用 <xref:Orleans.Streams.StreamSubscriptionHandle`1.UnsubscribeAsync*> 移除订阅。
- 更换 `PubSubStore` 之前，请规划如何重新创建现有显式订阅。

有关内部会合机制与拉取代理的设计，请参阅 [Orleans 流的实现](../implementation/streams-implementation/index.md)。
