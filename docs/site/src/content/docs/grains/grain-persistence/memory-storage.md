---
title: "内存 grain 持久性"
description: "用于测试和开发的配置内模 Orleans grain 存储 。"
ms.date: 08/07/2026
ms.topic: how-to
---

<a id="memory-grain-persistence"></a>

# 内存 grain 持久性

`Microsoft.Orleans.Persistence.Memory` 包仓库 grain 在 Orleans 组群中状态。用于测试、样品和可支配的当地开发环境。

> [!WARNING]
> 内存 grain 存储不是一个生产耐久机制。 记录分布在集束存储 grains 中, 但没有复制。 丢失主机或重新启动主机 silo 将丢失指定给其存储分区的记录, 而重新启用组群则会丢失全部记录 。

<a id="configure-a-named-provider"></a>

## 配置一个命名的提供者

配置一个有 <xref:Orleans.Hosting.MemoryGrainStorageSiloBuilderExtensions.AddMemoryGrainStorage*>的名提供者 :

:::code language="csharp" source="./snippets/persistence/StorageConfiguration.cs" id="configure_memory":::

提供者名称必须与 `[PersistentState]`中的存储名匹配。

<a id="configure-the-default-provider"></a>

## 配置默认的提供商

使用 <xref:Orleans.Hosting.MemoryGrainStorageSiloBuilderExtensions.AddMemoryGrainStorageAsDefault*> 何时 grains 使用内存存储时, 不应指定存储名称 :

:::code language="csharp" source="./snippets/persistence/StorageConfiguration.cs" id="configure_memory_default":::

<a id="storage-behavior"></a>

## 存储行为

<xref:Orleans.Configuration.MemoryGrainStorageOptions.NumStorageGrains> controls how many internal storage grains distribute the records. The default is 10 and the value must be greater than zero.

内存存储仍然通过 <xref:Orleans.Storage.IGrainStorageSerializer>进行序列状态序列化, 这样它就可以在测试中暴露序列化和系统错误。 它不模拟外部生产商店的时空、可用性、容量、通货率、共通货币或操作行为。
