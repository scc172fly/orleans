---
title: "Redis 日记账存储"
description: "配置 Redis 作为实验 Orleans 日报耐久状态的存储提供者 。"
ms.date: 10/19/2026
ms.topic: how-to
---

<a id="redis-journal-storage"></a>

# Redis 日记账存储

Orleans 定有的期刊数据被重放,以恢复由 <xref:Orleans.Durable.Journaling.IDurableStateManager>普通 grains 能够给管理器注入,并且 <xref:Orleans.Durable.Journaling.DurableGrain> 提供方便基级。 [日刊概览](index.md) 方案编制模式和 [事件递增比较](../event-sourcing/index.md#event-sourcing-and-experimental-journaling) 用于两个特征之间的边界。

预发 `Microsoft.Orleans.Durable.Redis` 软件包中存储 [Redis](https://redis.io/docs/latest/develop/data-types/)中的日记数据。

<a id="configure-redis-journal-storage"></a>

## 配置 Redis 日记存储

配置提供方的 <xref:Orleans.Durable.Journaling.RedisJournalStorageHostingExtensions.AddRedisJournalStorage*>:

:::code language="csharp" source="./snippets/redis-journaling/RedisJournalingConfiguration.cs" id="configure_redis_journaling":::

[可运行可运行 Redis 日报样本](samples.md#run-the-redis-sample) 使用具有 <xref:Orleans.Durable.Journaling.DurableGrain>的同一供应商,确认耐久价值更新、停止 grain和核实新启动后的恢复。

名称超载可接受一个服务商名, 后由选项代表跟随 。
使用独立连接或密钥前缀的单独名称进行单独
命名提供者的选项、目录、存储和
州管理工厂的解决方案是:
配置默认默认 grain 日记日记日记。 设置 `Orleans:Journaling:{name}:ProviderType` 至
`Redis` 配置每个命名的存储装订和使用
从依赖关系中选择客户连接的`Orleans:Journaling:{name}:ServiceKey`
。使用 `Default` 作为 grain 日记名,例如
`Orleans:Journaling:Default:ProviderType`.

持久工作任务可选择 Redis 写作提供者,并保留另一个提供者
将旧的前缀、证书和突变权限保留到每个
解决旧碎片:索赔、重理、取消、压缩和删除
正在继续。 Redis 目录扫描有现场列名语义,非常成功
库存是一种观察,而不是交易冻结的集束观点。
[移徙持久就业储存](durable-jobs-migration.md),包括
在所有时间表 silos 被切换后,重复的完整库存清单。

<a id="storage-behavior"></a>

## 存储行为

提供商在 Redis 字符串中存储期刊数据,并在 Redis hashes中存储日记元。每个杂志读和突变都使用原子卢阿脚本。目录操作通过扫描每个连接的主 Redis 服务器上的元数据键来发现日记。

最佳的同价通货法保护附加、 替换和删除操作。 一个老化的写作员接收 <xref:Orleans.Storage.InconsistentStateException>, 并定期发布国家经理的围栏作业和请求解除 grain 。 重新激活可以回收存储的日记。 同时读取在替换或完全替换之前观察日记 。

<xref:Orleans.Durable.Journaling.RedisJournalStorageOptions> supports:

- <xref:Orleans.Durable.Journaling.RedisJournalStorageOptions.ConfigurationOptions> for the StackExchange.Redis connection.
- <xref:Orleans.Durable.Journaling.RedisJournalStorageOptions.CreateMultiplexer> to supply a shared or custom connection multiplexer.
- <xref:Orleans.Durable.Journaling.RedisJournalStorageOptions.KeyPrefix> to isolate journal keys. The default is `{ServiceId}/journaling`.
- <xref:Orleans.Durable.Journaling.RedisJournalStorageOptions.GetKeyName> to customize the journal key component.
- <xref:Orleans.Durable.Journaling.RedisJournalStorageOptions.CompactionThresholdBytes> to select when the provider requests compaction.
- <xref:Orleans.Durable.Journaling.RedisJournalStorageOptions.ReadChunkSize> to limit the size of recovery segments supplied to the journal consumer.

关键前缀和密钥名函数定义现有日记的位置。 在部署新映射之前, 保留以前的映射或迁移存储的数据 。

默认压缩阈值为128 MiB ,默认回收块大小为 1 MiB。每个日记是一个 Redis 字符串,因此整个日记(包括一个附件)符合服务器字符串尺寸限制(默认情况下为512 MB) 。在脚本更改任何密钥之前超过此限制的写作失败了。压缩将附加历史替换为当前 grain耐久状态的快照。

Redis 返回日记字符串为一值。 <xref:Orleans.Durable.Journaling.RedisJournalStorageOptions.ReadChunkSize> 在检索后将该值除以重放段; 能力规划应说明 Redis 客户端和 silo 程序中的完整日记值。

<a id="durability"></a>

## 易易用性

Redis 日记的耐久性取决于 Redis [持久性](https://redis.io/docs/latest/operate/oss_and_stack/management/persistence/) 和复制配置。根据应用程序的回收点要求,配置持久性,例如只附文件(AOF),并采用适当的 `appendfsync` 政策。

使用不同的 <xref:Orleans.Durable.Journaling.RedisJournalStorageOptions.KeyPrefix> 多个 Orleans 共享服务 Redis 部署。

一致备份每个日记的字符串数据和散列元数据。 在允许 silos 激活 grain之前, 在同一前缀和密钥映射下恢复 。

能力、升级、备份和排除故障指南见 [操作和拼出故障 Orleans](operations.md)。
