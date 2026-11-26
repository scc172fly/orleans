---
title: "Azure 储存 grain 持久性"
description: "配置 Azure 表格存储或 Azure 布罗布存储 Orleans grain 状态。"
ms.date: 08/02/2026
ms.topic: how-to
---

<a id="azure-storage-grain-persistence"></a>

# Azure 储存 grain 持久性

[`Microsoft.Orleans.Persistence.AzureStorage`](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.AzureStorage) 套件包含 [Azure 表格储存](https://learn.microsoft.com/azure/storage/tables/table-storage-overview) 和 [Azure 布线存储](https://learn.microsoft.com/azure/storage/blobs/storage-blobs-introduction)的供应商。两者都使用存储ETags实施乐观同价计算法。

<a id="azure-table-storage"></a>

## Azure 表格储存

表格存储保存一个实体的国家记录,必要时在财产之间分解序列状态。 [Azure 表格储存将一个实体限制在1 MiB](https://learn.microsoft.com/azure/storage/tables/table-storage-overview#table-storage-concepts),因此使用布洛布存储或另一个提供商来获取更大的记录。

配置一个有 <xref:Orleans.Hosting.AzureTableSiloBuilderExtensions.AddAzureTableGrainStorage*>的命名提供者。 将调试证书比机密优先:

:::code language="csharp" source="../../snippets/compiled/Grains/PersistenceSnippets.cs" id="azure_identity_using_table":::

:::code language="csharp" source="../../snippets/compiled/Grains/PersistenceSnippets.cs" id="configure_azure_table_storage":::
指定存储账户身份,以及读写表格实体所需的数据-飞机权限。

<a id="azure-blob-storage"></a>

## Azure 布线存储

Blob存储将每个州的记录都保存在一团积水中,当国家超过表存储实体限值时是适当的:

:::code language="csharp" source="../../snippets/compiled/Grains/PersistenceSnippets.cs" id="azure_identity_using_blob":::

:::code language="csharp" source="../../snippets/compiled/Grains/PersistenceSnippets.cs" id="configure_azure_blob_storage":::
指定存储账户身份,指定读写布面所需的数据机权限。

<a id="connection-strings"></a>

## 连接字符串

连接字符串对本地模拟器和受限环境有用, 但含有秘密,

:::code language="csharp" source="./snippets/persistence/StorageConfiguration.cs" id="configure_connection_string":::

当连接字符串不可避免时使用秘密商店 。

<a id="operational-guidance"></a>

## 业务指导

- 当记录属于不同账户、集装箱或表格时,使用单独的供应商名称。
- 将 <xref:Orleans.Storage.InconsistentStateException> 作为乐观货币冲突处理,而不是短暂超时。
- 根据应用程序的耐久性要求配置存储冗余和账户错开。
- 使用序列式生产形状状态的试验记录尺寸限制。
