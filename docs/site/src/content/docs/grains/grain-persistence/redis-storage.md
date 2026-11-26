---
title: "Redis grain 持久性"
description: "配置 Redis 作为 Orleans grain 存储提供者 。"
ms.date: 08/07/2026
ms.topic: how-to
---

<a id="redis-grain-persistence"></a>

# Redis grain 持久性

下 [`Microsoft.Orleans.Persistence.Redis`](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.Redis) 软件包仓库 grain 状态内 [Redis](https://redis.io/docs/latest/develop/data-types/)提供商使用原子 Redis 拒绝简陋写作的脚本和ETags。

<a id="configure-a-named-provider"></a>

## 配置一个命名的提供者

配置有 <xref:Orleans.Hosting.RedisSiloBuilderExtensions.AddRedisGrainStorage*>的提供者 :

:::code language="csharp" source="./snippets/persistence/StorageConfiguration.cs" id="configure_redis":::

提供者名称必须与 `[PersistentState]`中的存储名匹配。

<a id="configure-the-default-provider"></a>

## 配置默认的提供商

使用 <xref:Orleans.Hosting.RedisSiloBuilderExtensions.AddRedisGrainStorageAsDefault*> 何时 grains 使用应 Redis 无需具体说明存储名称:

:::code language="csharp" source="./snippets/persistence/StorageConfiguration.cs" id="configure_redis_default":::

<a id="storage-behavior"></a>

## 存储行为

<xref:Orleans.Persistence.RedisStorageOptions> supports:

- <xref:Orleans.Persistence.RedisStorageOptions.ConfigurationOptions> for the StackExchange.Redis connection.
- <xref:Orleans.Persistence.RedisStorageOptions.DeleteStateOnClear> to delete the Redis key instead of resetting the record.
- <xref:Orleans.Persistence.RedisStorageOptions.EntryExpiry> to expire records.
- <xref:Orleans.Persistence.RedisStorageOptions.CreateMultiplexer> to supply a shared or custom `IConnectionMultiplexer`.
- <xref:Orleans.Persistence.RedisStorageOptions.GetStorageKey> to customize key generation.
- <xref:Orleans.Persistence.RedisStorageOptions.GrainStorageSerializer> to customize the stored representation.

默认密钥格式为 `{ServiceId}/state/{grainId}/{grainType}`。

> [!WARNING]
> 设定 <xref:Orleans.Persistence.RedisStorageOptions.EntryExpiry> 只为有意的短期状态, 如测试。 过期去除状态独立于 grain 生命周期, 并允许重复激活 。

更改关键函数或序列键不会迁移现有的 Redis 条目。计划数据迁移或保持与先前的表达式的兼容性。
