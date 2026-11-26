---
title: "逻辑一致性提供者"
description: "比较内置 Orleans 事件自动递增日志一致性提供者。"
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="log-consistency-providers"></a>

# 逻辑一致性提供者

`Microsoft.Orleans.EventSourcing` 包括三个提供者:

| 提供商 | 长期代表制 | <xref:Orleans.EventSourcing.JournaledGrain`2.RetrieveConfirmedEvents*> | 比额表审议 |
|---|---|---|---|
| 国家储存 | 当前状态快照、 版本、 元数据 | 无 | 读写完整的国家记录 |
| 日账储存 | 完整事件序列和元数据记录 | 是 是 | 读取和写入完整事件序列 |
| 自定义存储 | Application-defined | 否, 通过 <xref:Orleans.EventSourcing.JournaledGrain`2> | 由执行决定 |

<a id="state-storage"></a>

## 国家储存

<xref:Orleans.EventSourcing.StateStorage.LogConsistencyProvider> persists a snapshot using a configured <xref:Orleans.Storage.IGrainStorage>. It stores the state, confirmed version, and metadata used to avoid duplication after failures.

当当前状态是持久性要求时使用它, 并且完全的事件历史也不需要。 因为每次更新都写了完整的快照, 大州会增加序列化、转移和存储成本。 事件检索无法提供是因为事件没有保留 。

<a id="log-storage"></a>

## 日账储存

<xref:Orleans.EventSourcing.LogStorage.LogConsistencyProvider> persists the complete event sequence as one object using <xref:Orleans.Storage.IGrainStorage>. It keeps the complete sequence in memory and writes the complete sequence on updates.

它支持 <xref:Orleans.EventSourcing.JournaledGrain`2.RetrieveConfirmedEvents*>, 但其成本随整个历史而增加。 用于样本、 测试或绑定的日志。 它不是一个附加优化的生产事件仓库, 不适合无限制的事件序列 。

<a id="custom-storage"></a>

## 自定义存储

<xref:Orleans.EventSourcing.CustomStorage.LogConsistencyProvider> calls the real
<xref:Orleans.EventSourcing.CustomStorage.ICustomStorageInterface`2> 方法
由 grain执行:

:::code language="csharp" source="../../snippets/compiled/EventSourcing/EventSourcingSnippets.cs" id="custom_storage_operations":::

<xref:Orleans.EventSourcing.CustomStorage.ICustomStorageInterface`2.ReadStateFromStorage*> 返回已确认版本和状态。 <xref:Orleans.EventSourcing.CustomStorage.ICustomStorageInterface`2.ApplyUpdatesToStorage*> 必须从原子角度比较 `expectedVersion` 并附加/应用所提供序列。 在版本冲突时返回 `false`

<xref:Orleans.EventSourcing.CustomStorage.ICustomStorageInterface`2.ClearStoredState*>
当供应商支持破坏性日志时,当应用程序拥有状态被清除
清除。

提供商在例外情况下重新进行重试。 如果已承诺存储但回复丢失, 同样的更新可以再次提交。 执行时必须使回溯功能成为无能或检测到重复提交。 在更新持久之前恢复成功违反了语义学 <xref:Orleans.EventSourcing.JournaledGrain`2.ConfirmEvents*> 。

使用自定义存储将附加事件存储、快照加事件、保留或特定应用程序迁移整合到一个优化的事件存储处。 执行过程拥有耐久性、同值货币、事件检索、压缩和系统演变。
