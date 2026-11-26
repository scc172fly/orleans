---
title: "Orleans grain 目录"
description: "选择并配置 Orleans grain 目录。"
ms.date: 08/02/2026
ms.topic: concept-article
---

<a id="orleans-grain-directories"></a>

# Orleans grain 目录

A grain 地图a grain 身份的识别符号 silo 正在运行此功能的当前主机 。 Orleans 当调用路线和协调单点活动时,咨询目录 grain 安置。

<a id="start-with-the-default-directory"></a>

## 以默认目录启动

Orleans 默认使用内置 `DistributedGrainDirectory` 。它保留在组群中的注册,并使用版本的会籍视图协调分割所有权。 它不需要外部服务 。

目录协调登记和所有权转让, 这样用户就可以在成员变更时就激活的位置达成一致。 保持 grain 操作的无能: 失败的呼叫和重写仍然可以重复应用工作 。

要选择 `LegacyGrainDirectory`调,调 <xref:Orleans.Hosting.CoreHostingExtensions.UseLegacyGrainDirectory*> 上每 silo.

<a id="local-to-distributed-directory-upgrade"></a>
<a id="legacy-to-distributed-directory-upgrade"></a>

对于版本升级,在 [Grain 目录迁移](../migration/grain-directory.md#legacy-to-distributed-directory-upgrade)之后。

<a id="which-grain-directory-should-you-use"></a>

## 您应该使用哪个 grain 目录?

使用插头目录, grain 类型需要不同的操作特性:

<a id="adonet-grain-directory"></a>
<a id="supported-databases"></a>
<a id="installation"></a>
<a id="adonetgraindirectoryoptions"></a>
<a id="database-setup"></a>

| 目录目录目录目录目录目录 | 套包 | 考虑在 |
|---|---|---|
| Redis | [`Microsoft.Orleans.GrainDirectory.Redis`](https://www.nuget.org/packages/Microsoft.Orleans.GrainDirectory.Redis) | 共享 Redis 服务已经满足了长期和可用性要求。 |
| Azure 表格储存 | [`Microsoft.Orleans.GrainDirectory.AzureStorage`](https://www.nuget.org/packages/Microsoft.Orleans.GrainDirectory.AzureStorage) | Azure 表是首选的共同支持服务。 |
| [Google 云烟火炉](configuration-guide/google-firestore-providers.md) | [`Microsoft.Orleans.GrainDirectory.Firestore`](https://www.nuget.org/packages/Microsoft.Orleans.GrainDirectory.Firestore) | 共用的消防所数据库已经满足了长期和可用性要求。 |
| ADO.NET | [`Microsoft.Orleans.GrainDirectory.AdoNet`](https://www.nuget.org/packages/Microsoft.Orleans.GrainDirectory.AdoNet) | Grain 地点应使用现有支持的关系数据库。 |
| 自自自 | 申请或第三方货包 | 应用程序有内置供应商没有满足的后端特定要求。 |

外部目录添加网络呼叫和另一个可用性依赖。 有选择地应用它们, 并测试启动- 重工作量 。

<a id="configuration"></a>

## 配置

<a id="grain-configuration"></a>
<a id="silo-configuration"></a>

以名称登记提供商,并在 grain 执行时选择:

:::code language="csharp" source="snippets/hosting/HostingExamples.cs" id="named_grain_directory":::

:::code language="csharp" source="snippets/hosting/HostingExamples.cs" id="grain_directory_attribute":::

Grain 类型 <xref:Orleans.GrainDirectory.GrainDirectoryAttribute> 继续使用默认目录。您可以注册多个命名的供应商,以不同的 grain 类型。

要替换所有未附加说明的 grain 类型的默认值,请使用供应商的 `Use...GrainDirectoryAsDefault` 扩展名,例如 <xref:Orleans.Hosting.RedisGrainDirectoryExtensions.UseRedisGrainDirectoryAsDefault*>、 <xref:Orleans.Hosting.AzureTableGrainDirectorySiloBuilderExtensions.UseAzureTableGrainDirectoryAsDefault*>或 <xref:Orleans.Hosting.AdoNetGrainDirectorySiloBuilderExtensions.UseAdoNetGrainDirectoryAsDefault*>。

命名目录也可以配置为 `Orleans:GrainDirectory:{name}` , 并安装了声明提供者 :

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

## 集群内名录高度一致

<a id="key-features"></a>
<a id="when-to-use"></a>

默认 `DistributedGrainDirectory` 是基于分隔幅度和会籍视图的一组内一个非常一致的目录。 <xref:Orleans.Hosting.CoreHostingExtensions.AddDistributedGrainDirectory*> 明确选择该目录,或为同一目录例登记另一个名称。

在整个集中配置默认和命名目录映射。多个名称共享相同的分布目录实例 。

分布式目录默认为每个 silo (<xref:Orleans.Configuration.GrainDirectoryOptions.PartitionsPerSilo> = `1`)分区。只有在测试了预期的组群大小和工作量后才能更改此值。

<xref:Orleans.Configuration.GrainDirectoryOptions.GetPartitionBoundaries> configures the function which assigns starting hash boundaries to numbered partitions. The default uses the silo's consistent hash for a single partition and cached uniform hashes in generation order for multiple partitions. A custom function returns an initialized <xref:System.Collections.Immutable.ImmutableArray`1> containing exactly the configured number of boundaries, with each index identifying a partition. Keep each partition's starting boundary stable throughout the silo's membership and use equivalent functions on every silo. The immutable array shares its backing storage safely with directory membership snapshots.

如果边界函数丢弃或返回无效结果, Orleans 停止会籍目录处理并向成员阅读者报告一个 <xref:Orleans.Runtime.OrleansConfigurationException> 和刷新电话。修改边界配置并重新启动受影响的 silos。

创建目录时, 分区计数和边界函数会被记录。 使组合群中两个部分保持一致 。

<a id="operational-guidance"></a>

## 业务指导

- 保持目录后端延时空低;激活和第一调延时空取决于此。
- 为集群总工作量和故障爆发提供外部目录。
- 不要使用 grain 目录作为应用国家存储。
- 保持 grain 激活和解除无能状态。
- 测试 silo 损失、滚动升级和全集群重新开工。
- 监测重复激活、目录、会员和提供者错误。

建筑背景见 [Grain 目录实施](../implementation/grain-directory.md)。
