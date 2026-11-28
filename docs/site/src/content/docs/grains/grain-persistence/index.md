---
title: "Grain 持久化"
description: "使用 IPersistentState 和存储提供程序持久化 Orleans Grain 状态。"
ms.date: 08/17/2026
ms.topic: overview
---

<a id="grain-persistence"></a>

# Grain 持久化

Orleans Grain 持久化独立于 Grain 激活实例存储应用程序状态。激活实例启动时，Orleans 会在调用 <xref:Orleans.Grain.OnActivateAsync*> 之前读取已配置的状态记录。当操作达到需要保证数据持久性的时点时，Grain 显式写入变更。

持久化机制特意设计为面向记录的抽象，而非对象关系映射（ORM）。Grain 可以使用多个具名状态记录，为不同记录选择不同的提供程序，也可以在所需查询或数据模型不适合 Grain 存储时直接访问数据库。

<a id="choose-a-provider"></a>

## 选择存储提供程序

官方提供程序可通过 [NuGet](https://www.nuget.org/packages?q=Orleans+Persistence) 获取，包括：

| 提供程序 | 包 | 典型用途 |
|---|---|---|
| [Azure Table 和 Blob Storage](azure-storage.md) | [`Microsoft.Orleans.Persistence.AzureStorage`](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.AzureStorage) | 托管在 Azure 上的状态记录 |
| [Azure Cosmos DB for NoSQL](azure-cosmos-db.md) | [`Microsoft.Orleans.Persistence.Cosmos`](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.Cosmos) | 全球分布式 Azure NoSQL 存储 |
| [Amazon DynamoDB](dynamodb-storage.md) | [`Microsoft.Orleans.Persistence.DynamoDB`](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.DynamoDB) | AWS 托管的键值存储 |
| [Google Cloud Firestore](google-firestore-storage.md) | [`Microsoft.Orleans.Persistence.Firestore`](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.Firestore) | Google Cloud 托管的文档存储 |
| [ADO.NET](relational-storage.md) | [`Microsoft.Orleans.Persistence.AdoNet`](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.AdoNet) | SQL Server、MySQL/MariaDB、PostgreSQL、Oracle 和 SQLite |
| [Redis](redis-storage.md) | [`Microsoft.Orleans.Persistence.Redis`](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.Redis) | 基于 Redis 的低延迟状态存储 |
| [内存](memory-storage.md) | `Microsoft.Orleans.Persistence.Memory` | 测试和可丢弃的开发环境 |

根据持久性、可用性、延迟、记录大小限制、运维工具及成本进行选择。内存存储无法在集群重启后保留数据。Redis 过期机制仅应当用于明确允许丢失的临时状态。

<a id="use-persistent-state"></a>

## 使用持久化状态

将 <xref:Orleans.Runtime.IPersistentState`1> 注入 Grain 构造函数，并使用 <xref:Orleans.Runtime.PersistentStateAttribute> 指定状态记录及存储提供程序：

:::code language="csharp" source="./snippets/persistence/GrainExamples.cs" id="user_grain_multiple_states":::

构造函数中还无法访问已加载的状态。Orleans 在调用 <xref:Orleans.Grain.OnActivateAsync*> 之前加载状态。此后：

- <xref:Orleans.Core.IStorage`1.State> 包含内存中的状态值。
- <xref:Orleans.Core.IStorage.RecordExists> 指示提供程序是否读取到了已存在的记录。
- <xref:Orleans.Core.IStorage.Etag> 包含提供程序的并发控制令牌（如果支持）。
- <xref:Orleans.Core.IStorage.ReadStateAsync*> 使用最新的存储值替换内存中的值。
- <xref:Orleans.Core.IStorage.WriteStateAsync*> 持久化当前值。
- <xref:Orleans.Core.IStorage.ClearStateAsync*> 根据提供程序的配置清空或删除记录。

每个操作还有一个接受 <xref:System.Threading.CancellationToken> 的重载。提供程序可以实现取消支持，但接口的默认实现会调用不带取消令牌的重载。

> [!IMPORTANT]
> 修改 <xref:Orleans.Core.IStorage`1.State> 只会改变激活实例的内存副本。如果调用方必须获得已持久化的结果，应在返回之前等待 <xref:Orleans.Core.IStorage.WriteStateAsync*> 完成。

<a id="configure-named-state"></a>

## 配置具名状态

在 Silo 中配置 `[PersistentState]` 引用的每一个存储提供程序名称：

:::code language="csharp" source="./snippets/persistence/StorageConfiguration.cs" id="configure_managed_identity":::

状态名称用于区分同一个 Grain 拥有的不同记录。提供程序名称用于选择对应的带键 <xref:Orleans.Storage.IGrainStorage> 服务注册。不同记录不必共享同一提供程序或底层存储。

当 `[PersistentState]` 省略存储名称时，Orleans 会改为解析默认的 <xref:Orleans.Storage.IGrainStorage> 注册。使用提供程序的 `Add*GrainStorageAsDefault` 扩展方法配置默认注册，或向相应的 `Add*GrainStorage` 扩展方法传入 <xref:Orleans.Providers.ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME> (`"Default"`)。其他任何名称都只会添加对应的具名提供程序，而不会配置默认提供程序。

默认注册和具名注册代表不同的用途，即使它们使用相同的提供程序类型及底层数据库。例如，基于 Grain 的流发布/订阅通常使用名为 `PubSubStore` 的提供程序，与 Grain 状态使用的默认提供程序彼此独立。如果一个 Silo 同时需要这两种用途，则应分别注册：

:::code language="csharp" source="./snippets/persistence/StorageConfiguration.cs" id="configure_adonet_default_and_pubsub":::

默认注册和 `PubSubStore` 注册可以共享配置，但二者并非彼此的别名。有关流服务的存储用途，请参阅[配置 PubSub 存储](../../streaming/pubsub-storage)。

<a id="consistency-and-atomicity"></a>

## 一致性与原子性

一次存储操作只针对一条状态记录。如果后端支持，提供程序会使用记录的 <xref:Orleans.Core.IStorage.Etag> 实现乐观并发控制。使用过期 ETag 执行写入或清除操作时，将引发 <xref:Orleans.Storage.InconsistentStateException>，而不是覆盖较新的值。

以下操作不构成一个原子操作：

- 写入同一个 Grain 的两个 <xref:Orleans.Runtime.IPersistentState`1> 实例。
- 写入不同 Grain 拥有的状态。
- 一次存储写入与一个外部副作用，例如发布消息。

如果操作要求跨多个 Grain 状态进行原子更新，请使用 [Orleans 事务](../transactions.md)和事务状态。对于存储与消息传递的组合操作，应设计应用层的 Outbox（发件箱）、Inbox（收件箱）或幂等协议。

<a id="failure-semantics"></a>

## 失败语义

<a id="activation-reads"></a>

### 激活时读取

如果初始读取失败，激活也会失败，Orleans 不会调用 <xref:Orleans.Grain.OnActivateAsync*>。触发该激活的请求将收到失败结果。错误或缺失的提供程序配置会导致 <xref:Orleans.Storage.BadProviderConfigException>。

<a id="explicit-reads-writes-and-clears"></a>

### 显式读取、写入和清除

存储操作失败时，返回的任务将进入故障状态。等待每个操作完成，以便将失败传递给 Grain 调用及其调用方。写入失败后，不要假定存储中的值已经发生改变。确切结果取决于提供程序和底层服务的故障情况。

<xref:Orleans.Storage.InconsistentStateException> 表示自当前激活实例上次读取记录以来，其他写入方已修改该记录。不要盲目使用过期状态重试同一次写入。应重新读取数据，根据新状态重新评估命令，只有当操作仍然有效时才执行写入。

对于暂时性服务故障，应在了解幂等性的应用层边界执行重试。相比重试任意一次存储写入，更推荐使用操作标识符重试原始命令。限制重试次数，采用退避策略，并在重试预算耗尽时保留异常。Orleans 不会为应用程序自动重试 <xref:Orleans.Runtime.IPersistentState`1> 操作。

<a id="state-and-schema-evolution"></a>

## 状态与架构演进

持久化数据的生命周期长于激活实例及部署周期。应将存储的数据表示形式视为具有版本的契约：

1. 以向后兼容的方式添加成员，并为缺失的数据保留默认值。
1. 在开始仅写入新表示形式之前，先部署能同时读取新旧两种表示形式的读取方。
1. 没有迁移方案时，不要重命名、删除或重新解释已存储的成员。
1. 使用当前部署版本写入的数据测试反序列化。
1. 使用事件溯源时，保留旧的事件类型及状态转换行为。

存储提供程序通过其选项公开 <xref:Orleans.Storage.IGrainStorageSerializer>。默认提供程序序列化器使用 JSON。自定义序列化器可以实现显式封装格式、版本字段或迁移逻辑，但更换序列化器不会自动迁移现有记录。

<span id="redis-grain-persistence"></span>

Redis 配置已移至 [Redis Grain 持久化](redis-storage.md)。

<span id="memory-storage"></span>

内存存储配置已移至[内存 Grain 持久化](memory-storage.md)。

<a id="legacy-grain-state-base-class"></a>

## 旧版 Grain 状态基类

为保持兼容性，仍支持 <xref:Orleans.Grain`1> 基类和 <xref:Orleans.Providers.StorageProviderAttribute>，但新代码应使用 <xref:Orleans.Runtime.IPersistentState`1>。构造函数注入支持多条状态记录，并使存储依赖关系更加明确。

<a id="implement-a-storage-provider"></a>

## 实现存储提供程序

自定义提供程序需要实现 <xref:Orleans.Storage.IGrainStorage>。使用 Orleans 的存储注册辅助方法注册具名提供程序，该方法使用 .NET 带键服务：

:::code language="csharp" source="../../snippets/compiled/Grains/PersistenceSnippets.cs" id="register_custom_grain_storage":::
提供程序必须：

- 读取时填充 <xref:Orleans.IGrainState`1.State>、<xref:Orleans.IGrainState`1.RecordExists> 和 <xref:Orleans.IGrainState`1.ETag>。
- 保证乐观并发控制语义，并在 ETag 冲突时抛出 <xref:Orleans.Storage.InconsistentStateException>。
- 仅在存储操作实际完成后，才将返回的任务标记为完成。
- 将后端故障传递给调用方，而不是返回成功结果。
- 明确定义并记录 <xref:Orleans.Storage.IGrainStorage.ClearStateAsync*> 是删除记录还是重置记录。
