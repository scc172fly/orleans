---
title: "Azure 宇宙DB grain 持久性"
description: "配置配置 Azure 宇宙 DB NoSQL 以 A 时 Orleans grain 储存提供者。"
ms.date: 08/02/2026
ms.topic: how-to
---

<a id="azure-cosmos-db-for-nosql-grain-persistence"></a>

# Azure 宇宙 NoSQL grain 持久性DB

[`Microsoft.Orleans.Persistence.Cosmos`](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.Cosmos) 包仓库 grain 状态作为 [Azure 宇宙 DB NoSQL](https://learn.microsoft.com/azure/cosmos-db/overview)中的物项。组合组别使用 [`Microsoft.Orleans.Clustering.Cosmos`](https://www.nuget.org/packages/Microsoft.Orleans.Clustering.Cosmos)配置;安装或配置组群不仅仅是在 grain 状态中使用宇宙DB。

<a id="configure-storage"></a>

## 配置存储

配置一个有 <xref:Orleans.Hosting.HostingExtensions.AddCosmosGrainStorage*>的名服务提供方。 Microsoft Entra 认证避免存储账户密钥:

:::code language="csharp" source="../../snippets/compiled/Grains/PersistenceSnippets.cs" id="azure_identity_using_cosmos":::

:::code language="csharp" source="../../snippets/compiled/Grains/PersistenceSnippets.cs" id="configure_cosmos_storage":::
<xref:Orleans.Persistence.Cosmos.CosmosOptions.DatabaseName> defaults to `Orleans`, <xref:Orleans.Persistence.Cosmos.CosmosOptions.ContainerName> defaults to `OrleansStorage`, and <xref:Orleans.Persistence.Cosmos.CosmosGrainStorageOptions.PartitionKeyPath> defaults to `/PartitionKey`. Provision the database and container before startup when <xref:Orleans.Persistence.Cosmos.CosmosOptions.IsResourceCreationEnabled> is `false`. Enabling resource creation is convenient for development but production provisioning is usually managed separately.

<a id="partitioning-and-indexing"></a>

## 分区和索引

<xref:Orleans.Persistence.Cosmos.DefaultDocumentIdProvider> 是为每个 grain 记录提供文档编号和分区密钥。要自定义任一值,请执行 <xref:Orleans.Persistence.Cosmos.IDocumentIdProvider> ,并使用通用 <xref:Orleans.Hosting.HostingExtensions.AddCosmosGrainStorage*?displayProperty=nameWithType> 超载来登记。

在数据存在后更改分区键路径或分区键算法是数据迁移。所有使用相同提供者名称的 silos 都必须就数据库、容器、分区键路径和提供者执行达成协议。

使用 <xref:Orleans.Persistence.Cosmos.CosmosGrainStorageOptions.StateFieldsToIndex> 选择选定的序列化状态字段进行索引编制。只有操作查询中使用的域:额外的索引增加写成本。 Grain 存储本身通过身份检索记录,不提供一般查询 API。

<a id="concurrency-and-deployment"></a>

## 货币和部署

供应商将宇宙DBETags地图绘制到 Orleans 以失败的状态 <xref:Orleans.Storage.InconsistentStateException>.

变化状态时:

- 保持配置的序列器能够读取现有项目。
- 安装兼容的阅读器,然后写出新的代表。
- 将集装箱变更、隔开钥匙变更和序列转换作为明确的迁移处理。
