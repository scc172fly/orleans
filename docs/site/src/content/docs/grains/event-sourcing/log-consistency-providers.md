---
title: 日志一致性提供程序
description: 对比 Orleans 事件溯源内置的日志一致性提供程序。
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="log-consistency-providers"></a>
# 日志一致性提供程序

`Microsoft.Orleans.EventSourcing` 包含三种提供程序：

| 提供程序 | 持久化表示 | <xref:Orleans.EventSourcing.JournaledGrain`2.RetrieveConfirmedEvents*> | 可扩展性考虑因素 |
|---|---|---|---|
| 状态存储 | 当前状态快照、版本、元数据 | 不支持 | 读写完整的状态记录 |
| 日志存储 | 存放在一条记录中的完整事件序列及元数据 | 支持 | 读写完整的事件序列 |
| 自定义存储 | 由应用程序定义 | 不支持通过 <xref:Orleans.EventSourcing.JournaledGrain`2> 检索 | 取决于具体实现 |

<a id="state-storage"></a>
## 状态存储

<xref:Orleans.EventSourcing.StateStorage.LogConsistencyProvider> 使用配置的 <xref:Orleans.Storage.IGrainStorage> 持久化快照。它保存状态、已确认版本，以及用于避免故障后重复追加的元数据。

当持久化要求仅涉及当前状态而不需要完整事件历史时，使用此提供程序。由于每次更新都会写入完整快照，较大的状态会增加序列化、传输及存储成本。由于事件不会被保留，因此无法检索事件。

<a id="log-storage"></a>
## 日志存储

<xref:Orleans.EventSourcing.LogStorage.LogConsistencyProvider> 使用 <xref:Orleans.Storage.IGrainStorage> 将完整事件序列作为一个对象持久化。它在内存中保留完整序列，并在更新时写入完整序列。

它支持 <xref:Orleans.EventSourcing.JournaledGrain`2.RetrieveConfirmedEvents*>，但其成本会随完整历史的增长而增加。可将其用于示例、测试或大小有限的日志。它不是针对追加操作优化的生产级事件存储，不适合无界事件序列。

<a id="custom-storage"></a>
## 自定义存储

<xref:Orleans.EventSourcing.CustomStorage.LogConsistencyProvider> 调用由 Grain 实现的实际
<xref:Orleans.EventSourcing.CustomStorage.ICustomStorageInterface`2> 方法：

:::code language="csharp" source="../../snippets/compiled/EventSourcing/EventSourcingSnippets.cs" id="custom_storage_operations":::

<xref:Orleans.EventSourcing.CustomStorage.ICustomStorageInterface`2.ReadStateFromStorage*> 返回已确认版本及状态。<xref:Orleans.EventSourcing.CustomStorage.ICustomStorageInterface`2.ApplyUpdatesToStorage*> 必须以原子方式比较 `expectedVersion` 并追加或应用传入的序列。版本冲突时返回 `false`。

当提供程序支持破坏性的日志清除操作时，<xref:Orleans.EventSourcing.CustomStorage.ICustomStorageInterface`2.ClearStoredState*>
清除由应用程序管理的状态。

提供程序在发生异常后会重试。如果存储已经提交但响应丢失，则可能再次提交同一更新。实现必须保证重试的幂等性，或检测重复提交。在更新实现持久化之前返回成功会违反 <xref:Orleans.EventSourcing.JournaledGrain`2.ConfirmEvents*> 的语义。

使用自定义存储可集成针对追加操作优化的事件存储、快照与事件结合的方案、数据保留策略或应用程序特定的迁移机制。具体实现负责持久性、并发控制、事件检索、压实以及架构演进。
