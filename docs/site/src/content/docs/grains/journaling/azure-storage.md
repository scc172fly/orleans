---
title: "Azure 供日报的存储供应商"
description: "配置 Azure 布罗布存储或 Azure 表格存储,用于实验 Orleans 日志。"
ms.date: 10/19/2026
ms.topic: how-to
---

<a id="azure-storage-providers-for-journaling"></a>

# Azure 供日报的存储供应商

释放前 `Microsoft.Orleans.Durable.AzureStorage` 软件包提供 Azure 布洛布存储和 Azure 表格存储实施。

使用 Microsoft 将工作量识别内容包含在托管环境中,并给予 silo 仅指定选定容器或表格所需的数据飞机权限。

<a id="named-storage-namespaces"></a>

## 命名存储名域

两种 Azure 登记方法都接受备选代表的名字。
独立注册的Blob客户/集装箱或表格客户/表
不同的名称,可以配置同一后端的多个提供方。
选项、存储、目录和国营管理工厂都具有同样的约束力。
未知超载配置默认 grain 日记; 命名注册
使该默认独立。

对于账户、集装箱、表格或后端切切换,保留旧命名空间
以及根据原装订的地图绘制,而新的持久工作则使用碎片
在排水过程中,旧账户仍然需要列出
有条件的所有权更新、日记突变和删除权限。
只给予只读访问权限, 无法成功排水 。
[移徙持久就业储存](durable-jobs-migration.md) 以便他们被投入火刑,
完整库存和退休标准。

<a id="azure-blob-storage"></a>

## Azure 布线存储

配置 <xref:Orleans.Durable.Journaling.AzureBlobStorageHostingExtensions.AddAzureBlobJournalStorage*> ,并有经认证的 <xref:Azure.Storage.Blobs.BlobServiceClient>:

:::code language="csharp" source="./snippets/journaling/JournalingConfiguration.cs" id="configure_azure_blob":::

每期期刊使用:

- 默认情况下在 `wal/<journalId>` 的附加布质。
- `checkpoints/<journalId>/<snapshotId>`检查站的无法变换的布。
- 确定当前检查站、日记格式和乐观货币状态的WAL元数据。

回收读取已公布的检查站,然后是WAL尾巴。替换者上传新的检查站,然后通过WAL元数据原子发布。供应商在 <xref:Orleans.Durable.Journaling.AzureBlobJournalStorageOptions.DeleteOldCheckpoints> 是 `true`(默认)时公布后,对过时的检查站进行最佳清理。

自定义 <xref:Orleans.Durable.Journaling.AzureBlobJournalStorageOptions.GetWalBlobName> 和 <xref:Orleans.Durable.Journaling.AzureBlobJournalStorageOptions.GetCheckpointBlobName> 以应用租户或应用程序前缀。保持绘图稳定,或者将每个引用的浮标及其元数据一起迁移。

每个附件都是一个附加块, 所以 Azure 将整批或全部承诺。 Azure 附加 blobs 限制附加区块大小和块号。 供应商接受一个编码的附加组, 最多100 MiB , 在发送任何请求前拒绝更大的分批。 它要求在49 000个指定区块之后进行压缩, 在50 000个区块服务限制之前保留额外的头部。

日记目录选择配置容器 `wal/` 命名空间中的附加页眉。 参加目录发现活动的自定义指定代表为每个日志制作 `wal/<journalId>` 。 单独的检查站名称空间将检查点排除在列表页面之外。 Raw journal- id 前缀和保守的 ASCII 将缩小单位和等级名空间账户本地列名的范围。

HNS 递归列表排序 `/` 比其它字符先于其他字符。目录保存了每个约束区共享的列表前缀, 当标点或目录分隔符影响订单时会扩大剩余后缀。 例如, 从 `a!` 到 `a/0` 的包容性范围从 `wal/a` 开始, 在交叉 `wal/a0`之后完成。 每个返回的候选人都要对照原始正序区域检查。 共享的前缀可以保持时间戳扫描的窄度; 宽度可以转移更多候选人。 两个账户类型使用相同的列表算法并配置 Blob 客户端 。

列表呼叫器可以要求每个身份的元数据快照。 Blob 列表列表在列表回复中预测 WAL 格式、 Etag 和调用者拥有的元数据。 该快照可以取代读取的单独元数据;有条件更新使用其 ETag 检测同时发生的变化。

<a id="azure-table-storage"></a>

## Azure 表格储存

配置 <xref:Orleans.Durable.Journaling.AzureTableStorageHostingExtensions.AddAzureTableJournalStorage*> ,并有经认证的 <xref:Azure.Data.Tables.TableServiceClient>:

:::code language="csharp" source="./snippets/journaling/JournalingConfiguration.cs" id="configure_azure_table":::

每期日记都有一个表格分区:

- 头列存储日记清单、格式、生成和货币交易目录。
- 有序的数据行存储已公布的生成版本的编码日记字节。
- 附加操作在一个实体组交易中进行新行和页眉更新。
- 替换后写下新一代, 原子上修改标题以发布它。 当 <xref:Orleans.Durable.Journaling.AzureTableJournalStorageOptions.DeleteOldGenerations> 是 `true`时, 提供者会完成前一代最努力的清理 。

单个附件批次限为 2 MiB, 它将行和页眉更新保留在一个实体集团交易中, 最多100个实体和4个 MiB。 供应商在发送任何请求之前拒绝较大批次。 光速替换可能超过此大小, 因为行可以在信头发布一代之前写成, 而读者只看到已出版的几代人 。

发布尝试失败后, 预设行会保留, 这样延迟的页眉更新仍然可以公布整整一代。 删除日记可回收分区保留的行 。

请求压缩的时间为 <xref:Orleans.Durable.Journaling.AzureTableJournalStorageOptions.CompactionRowCountThreshold> (默认情况下为10,000行)或 <xref:Orleans.Durable.Journaling.AzureTableJournalStorageOptions.CompactionSizeThreshold> (默认情况下为32 MiB )。

默认分区映射可接受可打印的 ASCII 日记 id (`0x20` 至 `0x7E`) , 并将每个字节编码为两个大写十六进制数字。 它保存索引化目录查询的顺序和前缀, 支持最多512个字符的日记 id。 在访问 Azure之前存储创建程序验证该代号。 此限制是默认表格映射所特有的 。

当需要不同的分区布局时自定义 <xref:Orleans.Durable.Journaling.AzureTableJournalStorageOptions.GetPartitionKey> 。 自定义映射支持其它日记代字母, 保持每个日记的独特性, 并满足 Azure 表格配对键限制。 他们目录查询过滤了 Canonical 日记代号属性, 可能需要扫描表 。

以元数据为基础的目录查询选择头页格式和来电者拥有的元数据及其ETag。 只使用身份的查询保留了较小的预测。 每一个返回的元数据快照都具有与直接读取的元数据相同的含义,在列出后可以变得陈旧。

<a id="optimistic-concurrency"></a>

## 乐观的通货货币

两种提供者都附加、 替换和删除了最后观察到的 Etag 上的操作。 一个存储实例, 恢复了一个不存在日记, 只在它仍然不在时创建它, 所以同时创建者会让写作失败。 只处理元数据的冲突会收到一个封闭的更新和重试。 日记- 内容冲突会提出 <xref:Orleans.Storage.InconsistentStateException>, 它永久地围住国家管理员并请求取消 grain 。 重新激活该可持久的日记可以播放新版本 。

配置仅重试元数据上限和后退的元数据,并配置相应的 `MaxMetadataOnlyConflictRetries`、 `MetadataOnlyConflictInitialBackoff`和 `MetadataOnlyConflictMaxBackoff` 选项。

<a id="backup-and-restore"></a>

## 备份和恢复

获取一个一致的供应商级备份 :

- 对于Blob存储, 保存WAL、以WAL元数据命名的检查站以及解释这些元数据所需的所有元数据。
- 用于表格存储,保存分区页眉和每行的出版版本。

在允许 silos 激活 grain之前恢复完整设置。验证具有代表性的日记重播和随后在一个孤立环境中进行压缩。
