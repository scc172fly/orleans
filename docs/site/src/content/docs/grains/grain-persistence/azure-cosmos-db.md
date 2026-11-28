---
title: Azure Cosmos DB Grain 状态持久化
description: 将 Azure Cosmos DB for NoSQL 配置为 Orleans Grain 存储提供程序。
ms.date: 08/02/2026
ms.topic: how-to
---

<a id="azure-cosmos-db-for-nosql-grain-persistence"></a>

# Azure Cosmos DB for NoSQL Grain 状态持久化

[`Microsoft.Orleans.Persistence.Cosmos`](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.Cosmos) 包将 Grain 状态作为项存储在 [Azure Cosmos DB for NoSQL](https://learn.microsoft.com/azure/cosmos-db/overview) 中。集群成员资格通过 [`Microsoft.Orleans.Clustering.Cosmos`](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.Cosmos) 单独配置；仅使用 Cosmos DB 存储 Grain 状态时，无须安装或配置 Cosmos DB 集群成员资格提供程序。

<a id="configure-storage"></a>

## 配置存储

使用 <xref:Orleans.Hosting.HostingExtensions.AddCosmosGrainStorage*> 配置命名提供程序。Microsoft Entra 身份验证可以避免存储账户密钥：

:::code language="csharp" source="../../snippets/compiled/Grains/PersistenceSnippets.cs" id="azure_identity_using_cosmos":::

:::code language="csharp" source="../../snippets/compiled/Grains/PersistenceSnippets.cs" id="configure_cosmos_storage":::
<xref:Orleans.Persistence.Cosmos.CosmosOptions.DatabaseName> 默认为 `Orleans`，<xref:Orleans.Persistence.Cosmos.CosmosOptions.ContainerName> 默认为 `OrleansStorage`，<xref:Orleans.Persistence.Cosmos.CosmosGrainStorageOptions.PartitionKeyPath> 默认为 `/PartitionKey`。当 <xref:Orleans.Persistence.Cosmos.CosmosOptions.IsResourceCreationEnabled> 为 `false` 时，请在启动前预配数据库和容器。启用资源创建功能便于开发，但生产环境的资源预配通常由独立流程管理。

<a id="partitioning-and-indexing"></a>

## 分区与索引

<xref:Orleans.Persistence.Cosmos.DefaultDocumentIdProvider> 为每条 Grain 记录推导出文档 ID 和分区键。若要自定义其中任一值，请实现 <xref:Orleans.Persistence.Cosmos.IDocumentIdProvider> 并使用泛型 <xref:Orleans.Hosting.HostingExtensions.AddCosmosGrainStorage*?displayProperty=nameWithType> 重载进行注册。

已有数据后再更改分区键路径或分区键算法，属于数据迁移。所有使用同一提供程序名称的 Silo 必须使用相同的数据库、容器、分区键路径及提供程序实现。

使用 <xref:Orleans.Persistence.Cosmos.CosmosGrainStorageOptions.StateFieldsToIndex> 选择需要纳入索引的序列化状态字段。仅为运维查询使用的字段建立索引：增加索引会提高写入成本。Grain 存储本身按标识检索记录，不提供通用查询 API。

<a id="concurrency-and-deployment"></a>

## 并发与部署

提供程序将 Cosmos DB ETag 映射为 Orleans ETag。过期激活实例发起的并发写入会因 <xref:Orleans.Storage.InconsistentStateException> 而失败。

演进状态结构时：

- 确保已配置的序列化器仍能读取现有项。
- 先部署兼容的读取端，再写入新的表示形式。
- 将容器、分区键及序列化器的变更视为需要显式执行的迁移。
