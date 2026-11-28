---
title: 内存 Grain 持久化
description: 为测试及开发环境配置 Orleans 内存 Grain 存储。
ms.date: 08/07/2026
ms.topic: how-to
---

<a id="memory-grain-persistence"></a>
# 内存 Grain 持久化

`Microsoft.Orleans.Persistence.Memory` 包在 Orleans 集群内部存储 Grain 状态。可将其用于测试、示例和可随时丢弃的本地开发环境。

> [!WARNING]
> 内存 Grain 存储并不是生产环境的持久化机制。记录分布在集群的多个存储 Grain 中，但不进行复制。承载存储分区的 Silo 丢失或重启会导致分配给这些分区的记录丢失；重启整个集群则会丢失所有记录。

<a id="configure-a-named-provider"></a>
## 配置命名提供程序

使用 <xref:Orleans.Hosting.MemoryGrainStorageSiloBuilderExtensions.AddMemoryGrainStorage*> 配置命名提供程序：

:::code language="csharp" source="./snippets/persistence/StorageConfiguration.cs" id="configure_memory":::

提供程序名称必须与 `[PersistentState]` 中指定的存储名称匹配。

<a id="configure-the-default-provider"></a>
## 配置默认提供程序

当 Grain 应在不指定存储名称的情况下使用内存存储时，请使用 <xref:Orleans.Hosting.MemoryGrainStorageSiloBuilderExtensions.AddMemoryGrainStorageAsDefault*>：

:::code language="csharp" source="./snippets/persistence/StorageConfiguration.cs" id="configure_memory_default":::

<a id="storage-behavior"></a>
## 存储行为

<xref:Orleans.Configuration.MemoryGrainStorageOptions.NumStorageGrains> 控制用于分布存储记录的内部存储 Grain 数量。默认值为 10，且必须大于零。

内存存储仍通过 <xref:Orleans.Storage.IGrainStorageSerializer> 序列化状态，因此可以在测试期间暴露序列化及架构错误。它无法模拟外部生产级存储的延迟、可用性、容量、并发特性或运行行为。
