---
title: Orleans Grain 目录
description: 选择和配置 Orleans Grain 目录。
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="orleans-grain-directories"></a>

# Orleans Grain 目录

Grain 目录将 Grain 标识映射到当前承载其激活实例的 Silo。Orleans 在路由调用及协调单激活实例 Grain 放置时查询此目录。

<a id="start-with-the-default-directory"></a>

## 从默认目录开始

Orleans 默认使用内置的 `DistributedGrainDirectory`。它将注册记录保存在集群中，并使用带版本的成员资格视图协调分区所有权。无须外部服务。

随着成员资格变化，目录协调注册和所有权转移，使各调用方就激活实例的位置达成一致。保持 Grain 操作的幂等性：失败的调用和重试仍可能重复执行应用程序工作。

若要选择 `LegacyGrainDirectory`，请在每个 Silo 上调用 <xref:Orleans.Hosting.CoreHostingExtensions.UseLegacyGrainDirectory*>。

<a id="local-to-distributed-directory-upgrade"></a>
<a id="legacy-to-distributed-directory-upgrade"></a>

对于版本升级，请遵循[Grain 目录迁移](../migration/grain-directory.md#legacy-to-distributed-directory-upgrade)。

<a id="which-grain-directory-should-you-use"></a>

## 应选择哪种 Grain 目录？

对于需要不同运维特性的 Grain 类型，可以使用可插拔目录：

<a id="adonet-grain-directory"></a>
<a id="supported-databases"></a>
<a id="installation"></a>
<a id="adonetgraindirectoryoptions"></a>
<a id="database-setup"></a>

| 目录 | 程序包 | 适用场景 |
|---|---|---|
| Redis | [`Microsoft.Orleans.GrainDirectory.Redis`](https://www.nuget.org/packages/Microsoft.Orleans.GrainDirectory.Redis) | 共享 Redis 服务已满足延迟和可用性要求。 |
| Azure Table Storage | [`Microsoft.Orleans.GrainDirectory.AzureStorage`](https://www.nuget.org/packages/Microsoft.Orleans.GrainDirectory.AzureStorage) | Azure Table 是首选共享后端服务。 |
| [Google Cloud Firestore](configuration-guide/google-firestore-providers.md) | [`Microsoft.Orleans.GrainDirectory.Firestore`](https://www.nuget.org/packages/Microsoft.Orleans.GrainDirectory.Firestore) | 共享 Firestore 数据库已满足延迟和可用性要求。 |
| ADO.NET | [`Microsoft.Orleans.GrainDirectory.AdoNet`](https://www.nuget.org/packages/Microsoft.Orleans.GrainDirectory.AdoNet) | Grain 位置应使用现有受支持的关系数据库。 |
| 自定义 | 应用程序或第三方程序包 | 应用程序有内置提供程序无法满足的特定后端需求。 |

外部目录会增加网络调用和额外的可用性依赖。应有选择地使用它们，并对大量激活操作的工作负载进行负载测试。

<a id="configuration"></a>

## 配置

<a id="grain-configuration"></a>
<a id="silo-configuration"></a>

按名称注册提供程序，并在 Grain 实现上选择该名称：

:::code language="csharp" source="snippets/hosting/HostingExamples.cs" id="named_grain_directory":::

:::code language="csharp" source="snippets/hosting/HostingExamples.cs" id="grain_directory_attribute":::

未应用 <xref:Orleans.GrainDirectory.GrainDirectoryAttribute> 的 Grain 类型继续使用默认目录。可以为不同 Grain 类型注册多个命名提供程序。

若要替换所有未标注特性的 Grain 类型的默认目录，使用提供程序的 `Use...GrainDirectoryAsDefault` 扩展方法，例如 <xref:Orleans.Hosting.RedisGrainDirectoryExtensions.UseRedisGrainDirectoryAsDefault*>、<xref:Orleans.Hosting.AzureTableGrainDirectorySiloBuilderExtensions.UseAzureTableGrainDirectoryAsDefault*> 或 <xref:Orleans.Hosting.AdoNetGrainDirectorySiloBuilderExtensions.UseAdoNetGrainDirectoryAsDefault*>。

安装声明式提供程序后，还可以在 `Orleans:GrainDirectory:{name}` 下配置命名目录：

```json
{
  "Orleans": {
    "GrainDirectory": {
      "durable-directory": {
        "ProviderType": "Redis",
        "ConnectionString": "redis.example.com:6380,ssl=true"
      }
    }
  }
}
```

<a id="strongly-consistent-in-cluster-directory"></a>

## 强一致性的集群内目录

<a id="key-features"></a>
<a id="when-to-use"></a>

默认 `DistributedGrainDirectory` 是基于分区范围和成员资格视图的强一致性集群内目录。<xref:Orleans.Hosting.CoreHostingExtensions.AddDistributedGrainDirectory*> 可显式选择它，或为同一个目录实例注册额外的名称。

在整个集群中一致地配置默认目录及命名目录的映射。多个名称共享同一个分布式目录实例。

分布式目录默认每个 Silo 一个分区（<xref:Orleans.Configuration.GrainDirectoryOptions.PartitionsPerSilo> = `1`）。仅在使用预期集群规模和工作负载进行测试后才更改此设置。

<xref:Orleans.Configuration.GrainDirectoryOptions.GetPartitionBoundaries> 配置为各编号分区分配起始哈希边界的函数。默认情况下，单分区使用 Silo 的一致性哈希，多分区使用按生成顺序缓存的均匀哈希。自定义函数应返回已初始化的 <xref:System.Collections.Immutable.ImmutableArray`1>，其中恰好包含配置数量的边界，且每个索引标识一个分区。在 Silo 的整个成员资格期间保持每个分区的起始边界稳定，并在每个 Silo 上使用等效函数。不可变数组可安全地与目录成员资格快照共享底层存储。

如果边界函数抛出异常或返回无效结果，Orleans 将停止目录成员资格处理，并向成员资格读取方和刷新调用报告 <xref:Orleans.Runtime.OrleansConfigurationException>。修正边界配置后重启受影响的 Silo。

分区数量和边界函数在创建目录时捕获。应使两者在整个集群中保持一致。

<a id="operational-guidance"></a>

## 运维指导

- 保持目录后端的低延迟；激活及首次调用延迟取决于它。
- 根据整个集群的总工作负载以及故障期间的突发负载为外部目录配置资源。
- 不要将 Grain 目录用作应用程序状态存储。
- 保持 Grain 激活和停用操作的幂等性。
- 测试 Silo 丢失、滚动升级和全集群重启。
- 监控重复激活、目录、成员资格及提供程序错误。

有关架构背景，请参阅[Grain 目录实现](../implementation/grain-directory.md)。
