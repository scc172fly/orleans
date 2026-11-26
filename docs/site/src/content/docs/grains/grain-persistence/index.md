---
title: "Grain 持久性"
description: "持久性 Orleans grain 状态使用 IPersistentState 储存提供者和储存提供者。"
ms.date: 08/17/2026
ms.topic: overview
---

<a id="grain-persistence"></a>

# Grain 持久性

Orleans grain 持久性存储应用程序状态独立于 grain 激活。启动时, Orleans 在调用 <xref:Orleans.Grain.OnActivateAsync*>之前读取其配置的状态记录。当操作达到耐久点时, grain 明确写出更改 。

持久性是有意的,它是一个记录取向的抽象,而不是一个目标关系映像仪。 A grain 可以使用多个命名的国家记录,使用不同的提供者获取不同的记录,或者当数据库需要查询或数据模型而该数据库不适合 grain 存储时直接访问。

<a id="choose-a-provider"></a>

## 选择提供者

官方提供者来自 [NuGet](https://www.nuget.org/packages?q=Orleans+Persistence) ,包括:

| 提供商 | 套包 | 典型使用 |
|---|---|---|
| [Azure 表格和布束存储](azure-storage.md) | [`Microsoft.Orleans.Persistence.AzureStorage`](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.AzureStorage) | Azure-- -- 托管国家记录 |
| [Azure 宇宙 DB NoSQL](azure-cosmos-db.md) | [`Microsoft.Orleans.Persistence.Cosmos`](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.Cosmos) | 全球分布 Azure NoSQL 储存 |
| [亚马逊 DynamoDB](dynamodb-storage.md) | [`Microsoft.Orleans.Persistence.DynamoDB`](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.DynamoDB) | AWS托管的关键价值存储 |
| [Google 云烟火炉](google-firestore-storage.md) | [`Microsoft.Orleans.Persistence.Firestore`](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.Firestore) | Google云托管文件存储 |
| [ADO.NET](relational-storage.md) | [`Microsoft.Orleans.Persistence.AdoNet`](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.AdoNet) | SQL 服务器、 MySQL/MariaDB、 PostgreSQL、甲骨文和SQLite |
| [Redis](redis-storage.md) | [`Microsoft.Orleans.Persistence.Redis`](https://www.nuget.org/packages/Microsoft.Orleans.Persistence.Redis) | 以 Redis 支持的低延迟状态  |
| [内存](memory-storage.md) | `Microsoft.Orleans.Persistence.Memory` | 试验和可支配发展环境 |

基于耐久性、可用性、延时性、记录大小限制、操作工具、成本选择。 存储在集重开中无法持久。 Redis 到期应只用于有意短暂的国家 。

<a id="use-persistent-state"></a>

## 使用持久性状态

注射 <xref:Orleans.Runtime.IPersistentState`1> 和 grain 并标明国家记录和供应商 <xref:Orleans.Runtime.PersistentStateAttribute>:

:::code language="csharp" source="./snippets/persistence/GrainExamples.cs" id="user_grain_multiple_states":::

国家无法在构建器内使用。 Orleans 在 <xref:Orleans.Grain.OnActivateAsync*>之前就装载它。 从那时起:

- <xref:Orleans.Core.IStorage`1.State> 包含记忆值。
- <xref:Orleans.Core.IStorage.RecordExists> indicates whether the provider read an existing record.
- <xref:Orleans.Core.IStorage.Etag> contains the provider's concurrency token, when supported.
- <xref:Orleans.Core.IStorage.ReadStateAsync*> 以最新存储值取代内存值。
- <xref:Orleans.Core.IStorage.WriteStateAsync*> 维持当前值。
- <xref:Orleans.Core.IStorage.ClearStateAsync*> 根据供应商配置清除或删除记录。

每个操作都有一个 <xref:System.Threading.CancellationToken> 超载。 提供者可以执行取消, 但默认接口执行代表超载无符号的代理 。

> [!IMPORTANT]
> 变异 <xref:Orleans.Core.IStorage`1.State> 只更改激活的模拟副本。 在调用者必须观察持久结果时, 请在返回前等待 <xref:Orleans.Core.IStorage.WriteStateAsync*> 。

<a id="configure-named-state"></a>

## 配置名称状态

配置每个引用的提供者名 `[PersistentState]` 排在《公约》 silo:

:::code language="csharp" source="./snippets/persistence/StorageConfiguration.cs" id="configure_managed_identity":::

状态名称区分了同一 grain拥有的记录。 服务商名选择了密钥 <xref:Orleans.Storage.IGrainStorage> 注册。 共享服务商或支持存储不需要不同的记录 。

当 `[PersistentState]` 省略存储名称时, Orleans 将解决默认的 <xref:Orleans.Storage.IGrainStorage> 登记。配置该注册时使用提供者的 `Add*GrainStorageAsDefault` 扩展名,或将 <xref:Orleans.Providers.ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME> (`"Default"`) 转接到相应的 `Add*GrainStorage` 扩展名。任何其他名字只添加了指定供应商;它不配置默认。

默认和命名的注册代表了不同的角色,即使它们使用相同的提供者类型和辅助数据库。例如, grain- 基于流条的阴道/子常规使用指定的提供者 `PubSubStore`,而不受 grain 状态使用的默认提供者的影响。当 silo 需要两种角色同时:

:::code language="csharp" source="./snippets/persistence/StorageConfiguration.cs" id="configure_adonet_default_and_pubsub":::

默认和 `PubSubStore` 注册可以共享配置, 但两个角色都不以其他别名为别名。 关于流动作用的细节, 请参见 [配置 PubSub 存储](../../streaming/pubsub-storage)。

<a id="consistency-and-atomicity"></a>

## 一致性和原子化

存储操作适用于一个国家记录。 <xref:Orleans.Core.IStorage.Etag> 使用乐观的组合货币, 后端支持它。 带有废旧 Etag 失败的写或清晰 <xref:Orleans.Storage.InconsistentStateException> 而不是覆盖更新值。

以下不是一次原子操作:

- 写入两个 <xref:Orleans.Runtime.IPersistentState`1> 同一 grain.
- 给不同 grains拥有的国家的写作。
- 存储书写和外部副作用,例如发布信息。

如果操作需要多个 grain 状态的原子更新, 请使用 [Orleans 交易](../transactions.md) 和交易状态。 对于存储附加信息, 请设计一个应用级别发件箱、 信箱或应急协议 。

<a id="failure-semantics"></a>

## 失败语义

<a id="activation-reads"></a>

### 活动读作

如果初始读作失败, 激活失败且 Orleans 不调用 <xref:Orleans.Grain.OnActivateAsync*>。 导致激活的请求会收到失败。 错误或缺失的供应商配置导致 <xref:Orleans.Storage.BadProviderConfigException>。

<a id="explicit-reads-writes-and-clears"></a>

### 明确读写、写写和清楚

存储失败 错误 返回的任务 。 等待每次操作, 以便让失败到达 grain 调用机及其调用器。 在写失败后, 不要假设存储的价值已经改变 。 准确的结果取决于提供者和潜在的服务故障 。

<xref:Orleans.Storage.InconsistentStateException> 是指自激活此操作后又一个作家更改了记录。 不要盲目地重写与旧状态相同的文字。 重新读取, 重新评价命令以适应新状态, 并且只有在操作仍然有效时才写入 。

对于瞬时服务故障, 重试属于理解不合格状态的应用边界。 在重新输入任意存储写入时, 最好先用操作标识符重试原始命令。 断转、 添加回卸并在重试预算用尽后保留例外 。 Orleans 不自动重试应用程序的 <xref:Orleans.Runtime.IPersistentState`1> 操作 。

<a id="state-and-schema-evolution"></a>

## 国家和政体演变

持久性超过活性活性及部署。

1. 以后向兼容的形式添加成员,并保存缺漏数据的默认值。
1. 部署读者,在只写新书之前接受新的和新的陈述。
1. 不要重新命名、删除或重新解释 储存的成员 没有移民计划。
1. 使用目前部署版本所写数据测试消毒。
1. 使用事件来源时保留旧事件类型和过渡行为。

存储提供者通过选项暴露 <xref:Orleans.Storage.IGrainStorageSerializer> 。默认提供者使用 JSON序列器。自定义序列器可以执行清晰信封、版本字段或迁移,但更改序列器不会自动迁移现有记录。

<span id="redis-grain-persistence"></span>

Redis 配置已移到 [Redis grain 持久性](redis-storage.md)。

<span id="memory-storage"></span>

内存存储配置已移到 [内存 grain 持久性](memory-storage.md)。

<a id="legacy-grain-state-base-class"></a>

## 遗产 grain 国家基准等级

<xref:Orleans.Grain`1> 基级和 <xref:Orleans.Providers.StorageProviderAttribute> 仍然支持兼容性,但新代码应使用 <xref:Orleans.Runtime.IPersistentState`1>。构造注入支持多个国家记录,并明确储存依赖性。

<a id="implement-a-storage-provider"></a>

## 实施存储提供者

自定义提供者实施 <xref:Orleans.Storage.IGrainStorage>。使用 Orleans的存储登记助手注册一个名牌供应商,该助手使用 .NET 键入服务:

:::code language="csharp" source="../../snippets/compiled/Grains/PersistenceSnippets.cs" id="register_custom_grain_storage":::
提供商必须:

- 读取时弹出 <xref:Orleans.IGrainState`1.State>、 <xref:Orleans.IGrainState`1.RecordExists>和 <xref:Orleans.IGrainState`1.ETag> 。
- 保护乐观货币语义学,并引发 <xref:Orleans.Storage.InconsistentStateException> 对埃塔格冲突的冲击。
- 只有在储存作业完成后,才完成每个返回的任务。
- 表面后端失败, 而不是返回成功 。
- 定义并记录删除或重置记录是否 <xref:Orleans.Storage.IGrainStorage.ClearStateAsync*> 。
