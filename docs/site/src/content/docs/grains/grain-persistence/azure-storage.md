---
title: Azure Storage Grain 状态持久化
description: 配置 Azure Table Storage 或 Azure Blob Storage，以存储 Orleans 的 Grain 状态。
ms.date: 08/02/2026
ms.topic: how-to
---

<a id="azure-storage-grain-persistence"></a>

# Azure Storage Grain 状态持久化

[`Microsoft.Orleans.Persistence.AzureStorage`](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.AzureStorage) 包包含 [Azure Table Storage](https://learn.microsoft.com/azure/storage/tables/table-storage-overview) 和 [Azure Blob Storage](https://learn.microsoft.com/azure/storage/blobs/storage-blobs-introduction) 的存储提供程序。两者均使用存储 ETag 实现乐观并发控制。

<a id="azure-table-storage"></a>

## Azure Table Storage

表存储将一条状态记录存储在一个实体中，并在必要时将序列化后的状态拆分到多个属性中。[Azure Table Storage 将单个实体的大小限制为 1 MiB](https://learn.microsoft.com/azure/storage/tables/table-storage-overview#table-storage-concepts)，因此较大的记录应使用 Blob Storage 或其他提供程序。

使用 <xref:Orleans.Hosting.AzureTableSiloBuilderExtensions.AddAzureTableGrainStorage*> 配置命名提供程序。优先使用令牌凭据而不是机密：

:::code language="csharp" source="../../snippets/compiled/Grains/PersistenceSnippets.cs" id="azure_identity_using_table":::

:::code language="csharp" source="../../snippets/compiled/Grains/PersistenceSnippets.cs" id="configure_azure_table_storage":::
向存储账户使用的身份授予读取和写入表实体所需的数据平面权限。

<a id="azure-blob-storage"></a>

## Azure Blob Storage

Blob 存储将每条状态记录存储在一个 Blob 中，适用于状态大小可能超过表存储实体限制的情况：

:::code language="csharp" source="../../snippets/compiled/Grains/PersistenceSnippets.cs" id="azure_identity_using_blob":::

:::code language="csharp" source="../../snippets/compiled/Grains/PersistenceSnippets.cs" id="configure_azure_blob_storage":::
向存储账户使用的身份授予读取和写入 Blob 所需的数据平面权限。

<a id="connection-strings"></a>

## 连接字符串

连接字符串适用于本地模拟器和受限环境，但其中包含机密，不应提交到源码仓库：

:::code language="csharp" source="./snippets/persistence/StorageConfiguration.cs" id="configure_connection_string":::

无法避免使用连接字符串时，请使用机密存储。

<a id="operational-guidance"></a>

## 运维建议

- 当记录分别存储在不同的账户、容器或表中时，请使用不同的提供程序名称。
- 将 <xref:Orleans.Storage.InconsistentStateException> 视为乐观并发冲突，而不是暂时性超时。
- 根据应用程序的持久性要求配置存储冗余与账户故障转移。
- 使用符合生产环境数据结构的序列化状态来测试记录大小限制。
