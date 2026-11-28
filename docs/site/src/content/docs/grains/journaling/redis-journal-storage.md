---
title: Redis 日志存储
description: 将 Redis 配置为实验性 Orleans Journaling 持久化状态的存储提供程序。
ms.date: 10/19/2026
ms.topic: how-to
---

<a id="redis-journal-storage"></a>
# Redis 日志存储

Orleans Journaling 将持久化状态变更保存为有序日志数据，通过重放这些数据恢复由 <xref:Orleans.Durable.Journaling.IDurableStateManager> 管理的内存持久化值及集合。普通 Grain 可以注入该管理器，<xref:Orleans.Durable.Journaling.DurableGrain> 则提供了便捷基类。有关其编程模型，请参阅 [Journaling 概述](index.md)；有关这两项功能之间的边界，请参阅[事件溯源对比](../event-sourcing/index.md#event-sourcing-and-experimental-journaling)。

预发行版 `Microsoft.Orleans.Durable.Redis` 包将日志数据存储到 [Redis](https://redis.io/docs/latest/develop/data-types/) 中。

<a id="configure-redis-journal-storage"></a>
## 配置 Redis 日志存储

使用 <xref:Orleans.Durable.Journaling.RedisJournalStorageHostingExtensions.AddRedisJournalStorage*> 配置提供程序：

:::code language="csharp" source="./snippets/redis-journaling/RedisJournalingConfiguration.cs" id="configure_redis_journaling":::

[可运行的 Redis Journaling 示例](samples.md#run-the-redis-sample)在 <xref:Orleans.Durable.Journaling.DurableGrain> 中使用同一个提供程序，确认持久化值的更新，停用 Grain，并验证新激活实例中的恢复。

命名重载接受提供程序名称以及后续的选项委托。对于相互独立的日志命名空间，使用不同的名称以及独立的连接或键前缀。命名提供程序的选项、目录、存储及状态管理器工厂会作为一组进行解析；未命名重载仍用于配置默认 Grain Journaling。将 `Orleans:Journaling:{name}:ProviderType` 设置为 `Redis`，即可配置各个命名存储绑定；使用 `Orleans:Journaling:{name}:ServiceKey` 从依赖注入中选择对应的客户端连接。对于 Grain Journaling，使用 `Default` 作为名称，例如 `Orleans:Journaling:Default:ProviderType`。

Durable Jobs（持久化作业）可以选择一个 Redis 写入提供程序，并保留另一个提供程序用于排空旧数据。在每个旧分片的待处理工作全部解决之前，保留旧前缀、凭据和修改权限，因为分片认领、重试、取消、压实及删除操作仍会在那里继续。Redis 目录扫描具有实时列举语义，因此成功完成的清点只是一次观测，并不是以事务方式冻结的集群视图。请遵循[迁移 Durable Jobs 存储](durable-jobs-migration.md)中的指导，包括在所有调度 Silo 切换后重复进行完整清点。

<a id="storage-behavior"></a>
## 存储行为

提供程序将日志数据存储为 Redis 字符串，将日志元数据存储为 Redis 哈希表。针对单个日志的读取及修改使用原子 Lua 脚本。目录操作通过扫描每个已连接的 Redis 主服务器上的元数据键来发现日志。

乐观并发控制保护追加、替换和删除操作。过时的写入者会收到 <xref:Orleans.Storage.InconsistentStateException>，Journaling 状态管理器会永久禁止该实例继续执行相关操作，并请求停用 Grain。新的激活实例会恢复已存储的日志。并发读取只能观察到替换前的日志，或完整替换后的日志。

<xref:Orleans.Durable.Journaling.RedisJournalStorageOptions> 支持以下选项：

- <xref:Orleans.Durable.Journaling.RedisJournalStorageOptions.ConfigurationOptions>：配置 StackExchange.Redis 连接。
- <xref:Orleans.Durable.Journaling.RedisJournalStorageOptions.CreateMultiplexer>：提供共享或自定义连接多路复用器。
- <xref:Orleans.Durable.Journaling.RedisJournalStorageOptions.KeyPrefix>：隔离日志键。默认值为 `{ServiceId}/journaling`。
- <xref:Orleans.Durable.Journaling.RedisJournalStorageOptions.GetKeyName>：自定义日志键组成部分。
- <xref:Orleans.Durable.Journaling.RedisJournalStorageOptions.CompactionThresholdBytes>：选择提供程序何时请求压实。
- <xref:Orleans.Durable.Journaling.RedisJournalStorageOptions.ReadChunkSize>：限制提供给日志使用者的恢复分段大小。

键前缀及键名称函数决定现有日志的位置。部署新映射之前，保留先前映射或迁移已存储的数据。

默认压实阈值为 128 MiB，默认恢复分块大小为 1 MiB。每个日志都是一个 Redis 字符串，因此整个日志（包括待追加的数据）必须符合服务器的字符串大小限制（proto-max-bulk-len，默认为 512 MB）。超出该限制的写入会在脚本更改任何键之前失败。压实以 Grain 当前持久化状态的快照替换追加历史。

Redis 将日志字符串作为一个值返回。<xref:Orleans.Durable.Journaling.RedisJournalStorageOptions.ReadChunkSize> 在检索后将该值划分为重放分段；容量规划应考虑 Redis 客户端及 Silo 进程中完整日志值所占用的资源。

<a id="durability"></a>
## 持久性

Redis 日志的持久性取决于 Redis [持久化](https://redis.io/docs/latest/operate/oss_and_stack/management/persistence/)及复制配置。根据应用程序的恢复点要求配置持久化，例如使用具有适当 `appendfsync` 策略的仅追加文件（AOF）持久化。

当多个 Orleans 服务共享 Redis 部署时，使用不同的 <xref:Orleans.Durable.Journaling.RedisJournalStorageOptions.KeyPrefix>。

以一致的方式备份每个日志的字符串数据及哈希元数据。允许 Silo 激活 Grain 之前，使用相同的前缀和键映射恢复这两部分数据。

有关容量、升级、备份及故障排除指导，请参阅[运维 Orleans Journaling 并进行故障排除](operations.md)。
