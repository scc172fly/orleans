---
title: Journaling 的 Azure Storage 提供程序
description: 为实验性 Orleans Journaling 配置 Azure Blob Storage 或 Azure Table Storage。
ms.date: 10/19/2026
ms.topic: how-to
---

<a id="azure-storage-providers-for-journaling"></a>

# Journaling 的 Azure Storage 提供程序

预发布的 `Microsoft.Orleans.Durable.AzureStorage` 包提供 Azure Blob Storage 和 Azure Table Storage 实现。

在托管环境中使用 Microsoft Entra 工作负载标识，并且仅向 Silo 标识授予所选容器或表所需的数据平面权限。

<a id="named-storage-namespaces"></a>

## 命名存储命名空间

两种 Azure 注册方法均接受一个名称及其后的选项配置委托。
使用不同名称注册独立的 Blob 客户端与容器，或 Table 客户端与表，
可以配置同一后端的多个提供程序。命名选项、存储、目录及状态管理器工厂
共享同一个绑定。
不带名称的重载配置默认的 Grain 日志持久化；命名注册
不会影响该默认配置。

切换账户、容器、表或存储后端时，应在原有绑定下保留旧命名空间
及其映射，并让新的 Durable Jobs 分片使用
第二个绑定。在排空期间，旧账户仍需具备列举、
条件性所有权更新、日志变更和删除权限。
仅授予读取权限会使排空无法成功。请参阅
[迁移 Durable Jobs 存储](durable-jobs-migration.md)，了解分阶段发布、
完整清单以及退役条件。

<a id="azure-blob-storage"></a>

## Azure Blob Storage

使用已通过身份验证的 Blob 客户端进行配置：调用 <xref:Orleans.Durable.Journaling.AzureBlobStorageHostingExtensions.AddAzureBlobJournalStorage*>，传入 <xref:Azure.Storage.Blobs.BlobServiceClient>。

:::code language="csharp" source="./snippets/journaling/JournalingConfiguration.cs" id="configure_azure_blob":::

每份日志使用：

- 默认位于 `wal/<journalId>` 的追加 Blob。
- 位于 `checkpoints/<journalId>/<snapshotId>` 的不可变检查点 Blob。
- WAL 元数据，用于标识当前检查点、日志格式及乐观并发状态。

恢复时先读取已发布的检查点，再读取 WAL 尾部。替换操作先上传新检查点，然后通过 WAL 元数据原子发布它。当 <xref:Orleans.Durable.Journaling.AzureBlobJournalStorageOptions.DeleteOldCheckpoints> 为 `true`（默认值）时，提供程序在发布后尽力清理过时的检查点。

自定义 <xref:Orleans.Durable.Journaling.AzureBlobJournalStorageOptions.GetWalBlobName> 和 <xref:Orleans.Durable.Journaling.AzureBlobJournalStorageOptions.GetCheckpointBlobName> 以应用租户或应用程序前缀。保持映射稳定，或者将每个被引用的 Blob 与其元数据一起迁移。

每次追加操作对应一个追加块，因此 Azure 要么提交整个批次，要么完全不提交。Azure 追加 Blob 限制追加块大小和块数。提供程序接受最多 100 MiB 的已编码追加批次，并在发送任何请求之前拒绝较大的批次。提交 49,000 个块后请求压实，并在 50,000 个块的服务上限前预留额外空间。

日志目录在配置的容器的 `wal/` 命名空间内选择追加 Blob。参与目录发现的自定义命名委托为每份日志生成 `wal/<journalId>`。独立的检查点命名空间使检查点不出现在列举结果页中。原始日志 ID 前缀及保守的 ASCII 边界可在平面命名空间和分层命名空间（HNS）账户上缩小原生列举范围。

HNS 递归列举将 `/` 排在其他字符之前。当标点符号或目录分隔符影响排序时，目录保留每个边界的共有列举前缀并扩大剩余后缀的范围。例如，从 `a!` 到 `a/0` 的包含端点的范围，从 `wal/a` 开始，在跨过 `wal/a0` 后结束。每个返回的候选项都要按照原始序数比较范围进行检查。共有前缀使时间戳扫描保持较窄的范围；更宽的边界可能传输额外的候选项。两种账户类型都使用相同的列举算法及配置的 Blob 客户端。

目录调用方可以请求每个标识对应的元数据快照。Blob 列举结果会在响应中投影 WAL 的格式、ETag 及调用方拥有的元数据。快照可以代替单独的元数据读取；条件更新使用其 ETag 检测并发变更。

<a id="azure-table-storage"></a>

## Azure Table Storage

使用已通过身份验证的 Table 客户端进行配置：调用 <xref:Orleans.Durable.Journaling.AzureTableStorageHostingExtensions.AddAzureTableJournalStorage*>，传入 <xref:Azure.Data.Tables.TableServiceClient>。

:::code language="csharp" source="./snippets/journaling/JournalingConfiguration.cs" id="configure_azure_table":::

每份日志占用一个表分区：

- 头部记录行保存日志清单、格式、世代及并发控制 ETag。
- 有序的数据记录行保存已发布世代对应的编码日志字节。
- 追加操作在同一个实体组事务中提交新记录行和头部更新。
- 替换操作写入新世代，并原子更新头部以发布该世代。当 <xref:Orleans.Durable.Journaling.AzureTableJournalStorageOptions.DeleteOldGenerations> 为 `true` 时，提供程序尽力清理上一世代。

单个追加批次限制为 2 MiB，使其记录行及头部更新保持在最多 100 个实体、4 MiB 的单个实体组事务内。提供程序在发送任何请求之前拒绝较大的批次。快照替换可以超过此大小，因为数据行在头部发布世代之前写入，而读取方仅能看到已发布世代。

发布尝试失败之后，暂存的数据行仍保留，以使延迟到达的头部更新仍然能够发布完整世代。删除日志会回收该分区中保留的所有数据行。

达到 <xref:Orleans.Durable.Journaling.AzureTableJournalStorageOptions.CompactionRowCountThreshold>（默认 10,000 行）或 <xref:Orleans.Durable.Journaling.AzureTableJournalStorageOptions.CompactionSizeThreshold>（默认 32 MiB）任一阈值时，都会请求压实。

默认分区映射接受可打印 ASCII 日志 ID（`0x20` 至 `0x7E`），并将每个字节编码为两个大写十六进制数字。它保留序数排序及前缀，以支持基于索引的目录查询，并支持最多 512 个字符的日志 ID。创建存储时会在访问 Azure 之前验证 ID。此限制仅适用于默认 Table 映射。

需要不同的分区布局时，可以自定义 <xref:Orleans.Durable.Journaling.AzureTableJournalStorageOptions.GetPartitionKey>。自定义映射支持其他日志 ID 字符集，对每份日志保持唯一，并满足 Azure Table 分区键约束。其目录查询会筛选规范的日志 ID 属性，可能需要扫描整个表。

启用元数据的目录查询会选取头部的格式及调用方拥有的元数据，同时选取其 ETag。仅查询标识的操作保留较小的投影。返回的每个元数据快照与直接读取元数据的含义相同，并且在列举完成之后可能变得陈旧。

<a id="optimistic-concurrency"></a>

## 乐观并发

两种提供程序均使用最后观察到的 ETag 作为追加、替换和删除操作的前置条件。恢复时发现日志不存在的存储实例仅在该日志仍不存在时才创建它，因此并发创建者会导致写入失败。仅涉及元数据的冲突会进行有界的原地刷新与重试。日志内容冲突会引发 <xref:Orleans.Storage.InconsistentStateException>，永久隔离 Journaling 状态管理器，并请求停用 Grain。新的激活实例会重放持久化日志。

使用相应的 `MaxMetadataOnlyConflictRetries`、`MetadataOnlyConflictInitialBackoff` 和 `MetadataOnlyConflictMaxBackoff` 选项配置仅涉及元数据冲突时的重试上限及退避策略。

<a id="backup-and-restore"></a>

## 备份与恢复

创建一致的提供程序级备份：

- 对于 Blob Storage，保留 WAL、WAL 元数据指定的检查点以及解释它们所需的全部元数据。
- 对于 Table Storage，保留分区头部以及已发布世代的所有数据行。

在允许 Silo 激活 Grain 之前恢复完整的数据集合。在隔离环境中验证代表性日志的重放及随后的压实操作。
