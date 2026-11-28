---
title: Redis Grain 持久化
description: 将 Redis 配置为 Orleans Grain 存储提供程序。
ms.date: 08/07/2026
ms.topic: how-to
---

<a id="redis-grain-persistence"></a>
# Redis Grain 持久化

[`Microsoft.Orleans.Persistence.Redis`](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.Redis) 包将 Grain 状态存储到 [Redis](https://redis.io/docs/latest/develop/data-types/) 中。提供程序使用 Redis 原子脚本和 ETag 拒绝过时的写入。

<a id="configure-a-named-provider"></a>
## 配置命名提供程序

使用 <xref:Orleans.Hosting.RedisSiloBuilderExtensions.AddRedisGrainStorage*> 配置提供程序：

:::code language="csharp" source="./snippets/persistence/StorageConfiguration.cs" id="configure_redis":::

提供程序名称必须与 `[PersistentState]` 中指定的存储名称匹配。

<a id="configure-the-default-provider"></a>
## 配置默认提供程序

当 Grain 应在不指定存储名称的情况下使用 Redis 时，请使用 <xref:Orleans.Hosting.RedisSiloBuilderExtensions.AddRedisGrainStorageAsDefault*>：

:::code language="csharp" source="./snippets/persistence/StorageConfiguration.cs" id="configure_redis_default":::

<a id="storage-behavior"></a>
## 存储行为

<xref:Orleans.Persistence.RedisStorageOptions> 支持以下选项：

- <xref:Orleans.Persistence.RedisStorageOptions.ConfigurationOptions>：配置 StackExchange.Redis 连接。
- <xref:Orleans.Persistence.RedisStorageOptions.DeleteStateOnClear>：清除状态时删除 Redis 键，而不是重置记录。
- <xref:Orleans.Persistence.RedisStorageOptions.EntryExpiry>：使记录过期。
- <xref:Orleans.Persistence.RedisStorageOptions.CreateMultiplexer>：提供共享或自定义的 `IConnectionMultiplexer`。
- <xref:Orleans.Persistence.RedisStorageOptions.GetStorageKey>：自定义键生成方式。
- <xref:Orleans.Persistence.RedisStorageOptions.GrainStorageSerializer>：自定义存储表示形式。

默认键格式为 `{ServiceId}/state/{grainId}/{grainType}`。

> [!WARNING]
> 仅为有意设置为临时状态的数据（例如测试数据）设置 <xref:Orleans.Persistence.RedisStorageOptions.EntryExpiry>。过期机制独立于 Grain 生命周期删除状态，并可能允许出现重复激活实例。

更改键生成函数或序列化器不会迁移现有 Redis 条目。应制定数据迁移计划，或保持与先前存储表示的兼容性。
