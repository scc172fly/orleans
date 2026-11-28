---
title: 配置 Orleans 日志化状态
description: 选择实验性 Orleans Journaling 的存储提供程序和日志格式。
ms.date: 10/19/2026
ms.topic: how-to
---

<a id="configure-orleans-journaling"></a>

# 配置 Orleans 日志化状态

在托管相应 Grain 类型的每个 Silo 上，配置这些 Grain 或使用 <xref:Orleans.Durable.Journaling.IDurableStateManager> 的激活作用域功能所选择的日志存储提供程序。命名提供程序为各 Grain 类型及独立日志使用方提供相互独立的存储命名空间。未显式选择提供程序的 Grain 类型使用 `Default`。注册提供程序还会添加 Journaling 核心服务、持久化状态工厂和键控服务、激活生命周期集成、JSON 格式以及 Orleans 二进制读取器。

`Microsoft.Orleans.Durable` 及其存储提供程序软件包均为预发行的 Alpha 软件包。

<a id="configure-grain-scoped-composition"></a>

## 配置 Grain 作用域内的组合

标准的 <xref:Orleans.Durable.Journaling.JournalingHostingExtensions.AddJournaling*> 注册方式利用现有的激活服务作用域，为每个激活实例提供一个作用域内的 `IDurableStateManager`。管理器使用激活实例的 <xref:Orleans.Runtime.IGrainContext> 构造时，将自身注册到 Grain 生命周期中。将此管理器注入普通 <xref:Orleans.Grain>、应用程序自定义的 Grain 基类或激活作用域内的功能组件。在构造期间或同步激活设置期间，使用其 `GetOrAdd` 辅助方法或键控注入声明状态。这样，<xref:Orleans.Runtime.GrainLifecycleStage.SetupState> 阶段的恢复会在 <xref:Orleans.Grain.OnActivateAsync*> 和请求处理之前完成。

注册存储后，这些服务在整个 Silo 中可用。直接解析管理器或通过持久化状态依赖项解析管理器的激活实例，会触发每个 Grain 的日志读写。不相关的 Grain 激活实例保留其现有的持久化行为。

<xref:Orleans.Durable.Journaling.DurableGrain> 为同一个作用域内的管理器提供可选的受保护辅助方法。默认管理器同时实现 `IDurableStateManager` 和独立的 <xref:Orleans.Durable.Journaling.IJournaledStateManager> 契约。自定义 Grain 管理器替代实现应按需提供这两种契约及其注册，并在构造函数或注册工厂中完成生命周期注册，再返回解析结果。可复用的功能通过[共享激活设置操作](../grain-lifecycle.md#shared-activation-setup)组合自己的生命周期工作。

<xref:Orleans.Durable.Journaling.IJournaledStateManagerFactory.CreateStandalone*> 为显式提供的状态机组件返回日志所有者契约。调用方构造并注册组件、初始化日志，以及释放所有者。组件依赖项及其生命周期仍由调用方管理。独立日志所有者不使用由管理器拥有的 DI 作用域。

有关以源码为依据的构造函数注入和功能设置示例，请参阅[使用持久化状态](durable-state.md)；有关生命周期顺序，请参阅[激活与恢复](runtime-behavior.md#activation-and-recovery)。

<a id="choose-a-storage-provider"></a>

## 选择存储提供程序

| 提供程序 | 软件包 | 存储模型 | 压实触发条件 | 单次追加大小上限 |
| --- | --- | --- | --- | --- |
| [Azure Blob Storage](azure-storage.md#azure-blob-storage) | `Microsoft.Orleans.Durable.AzureStorage` | 追加 Blob 加不可变检查点 Blob | 追加 Blob 的块数预算 | 100 MiB |
| [Azure Table Storage](azure-storage.md#azure-table-storage) | `Microsoft.Orleans.Durable.AzureStorage` | 每个日志一个分区，包含头记录及有序数据行 | 行数或日志字节数 | 2 MiB |
| [Redis](redis-journal-storage.md) | `Microsoft.Orleans.Durable.Redis` | 字符串日志加哈希元数据 | 日志字节数 | 整个日志不超过服务器的字符串大小限制（默认为 512 MB） |
| Amazon S3 | `Microsoft.Orleans.Durable.S3` | WAL 对象加不可变检查点对象 | 追加分段数预算 | 5 GB |

所有提供程序均以全有或全无的方式执行每次追加和替换操作，超过其限制的写入将整体失败；参阅[日志存储契约](runtime-behavior.md#journal-storage-contract)。根据原子写入限制、重放延迟、持久性配置、备份工具、热点 Grain 最大大小和运维熟悉程度选择提供程序。

<a id="select-named-providers"></a>

## 选择命名提供程序

Blob、Table、Redis、S3 和易失性存储的注册 API 均接受提供程序名称。每次命名注册都会将选项、存储、目录和状态管理器工厂绑定到一个物理日志命名空间。无名称的重载配置 `Default` 绑定。其他名称的注册不改变此默认绑定。

注册命名后端时，同时提供提供程序名称和选项配置委托。如果在其他位置单独配置选项，则传入 `configure: null`。

`Default` 存储绑定保留未命名后端选项的配置管道，包括在便捷方法之前或之后注册的选项委托，每个 `ConfigureAll` 委托均应用一次。非默认存储名称使用各自对应的命名后端选项。因此，对于默认绑定，存储绑定名称与后端选项名称是不同的。

自定义的非键控单例 <xref:Orleans.Durable.Journaling.IJournalStorageProvider> 同时为 Grain 激活实例和 `Default` 键控工厂提供存储。默认的目录及生命周期绑定也使用该提供程序，因此替代提供程序需要实现注册的提供程序类型所声明的目录及生命周期契约。

提供程序名称标识存储，而非日志格式或某个持久化状态。每个选定的物理命名空间只注册一次；当其中仍有工作时，保持其账户、容器、表、存储桶、键前缀及命名函数稳定。

在 `Orleans:Journaling:{name}` 下配置每个日志存储绑定。`ProviderType` 选择其存储提供程序，`ServiceKey` 从依赖注入中选择 Azure 或 Redis 客户端。Orleans 将每项的名称及配置节传递给已注册的提供程序构建器。对于采用默认选择的 Grain 类型使用 `Default`，对于选定的其他存储绑定使用额外的名称。

例如，以下配置项为默认日志及归档日志配置分别注册的 Redis 客户端和键前缀：

```json
{
  "Orleans": {
    "Journaling": {
      "Default": {
        "ProviderType": "Redis",
        "ServiceKey": "primary-redis",
        "KeyPrefix": "primary-journal"
      },
      "archive": {
        "ProviderType": "Redis",
        "ServiceKey": "archive-redis",
        "KeyPrefix": "archive-journal"
      }
    }
  }
}
```

归档客户端对应的环境变量键为 `Orleans__Journaling__archive__ServiceKey`。

<a id="select-storage-per-grain-type"></a>

### 按 Grain 类型选择存储

将 <xref:Orleans.Durable.Journaling.JournalStorageProviderAttribute> 应用于 Grain 类，以选择已注册的提供程序名称（采用序数比较，区分大小写）。Orleans 将该选择发布到 Grain 类型元数据中，并在创建激活实例的共享状态管理器时解析它。继承的特性提供基类的选择；派生 Grain 类上的特性提供该类型自己的选择。

在托管相关 Grain 类型的每个 Silo 上注册相同的名称及物理命名空间。例如，两个命名 Azure Blob 提供程序可以在默认容器之外使用各自独立的容器：

:::code language="csharp" source="./snippets/journaling/GrainJournalProviders.cs" id="configure_grain_providers":::

<xref:Orleans.Durable.Journaling.DurableGrain> 辅助方法和构造函数注入都使用选定的提供程序：

:::code language="csharp" source="./snippets/journaling/GrainJournalProviders.cs" id="select_grain_providers":::

所有注入的持久化值、集合及由日志存储支持的 <xref:Orleans.Runtime.IPersistentState`1> 都共享该激活实例的管理器和写入边界。提供程序在激活实例的整个生命周期中保持不变。如果选择无效或未注册，则在访问日志数据之前引发配置错误，其中标明 Grain 类型及提供程序。

恢复操作使用 Grain 现有的日志标识读取选定的物理命名空间。改变 Grain 类型的选择会将新激活实例指向新的命名空间。对于已存在日志的 Grain 类型，应有计划地迁移数据或进行切换：停止对旧命名空间的写入，迁移并验证日志及其元数据，在所有 Silo 上一致地部署新的选择。将提供程序选择和命名空间纳入回滚计划。

对于持久化作业（Durable Jobs），<xref:Orleans.Hosting.DurableJobsExtensions.UseJournaledDurableJobs*> 选择基于日志的实现。<xref:Orleans.Hosting.DurableJobsOptions.ActiveProviderName> 选择新分片使用的提供程序，默认为 `Default`。<xref:Orleans.Hosting.DurableJobsOptions.DrainingProviderNames> 选择包含现有分片的其他提供程序，默认为空。所有选定的绑定都需要存储、目录及工厂服务，并在启动时验证。绑定在该进程整个生命周期中保持不变。

有关命名提供程序选择、分阶段部署和整个命名空间退役检查，请参阅[迁移持久化作业存储](durable-jobs-migration.md)。该指南链接的可运行示例使用此仓库构建的软件包演练新的 API。Journaling 代码片段项目引用当前仓库的源码，使配置示例使用当前检出的 API。

<a id="configure-the-json-format"></a>

## 配置 JSON 格式

JSON Lines 是默认的写入格式。<xref:Orleans.Durable.Journaling.Json.JsonJournalHostingExtensions.UseJsonJournalFormat*> 为用作持久化键、值、集合元素、持久化状态或持久化任务结果的应用程序类型注册源生成的元数据：

:::code language="csharp" source="./snippets/journaling/JournalingConfiguration.cs" id="configure_json_format":::

该格式输出 UTF-8 编码的 JSON Lines，每行包含一个完整的日志条目；如果提供程序支持内容类型，则使用 `application/jsonl` 元数据。

序列化器命名策略影响应用程序有效载荷值。日志命令名称和记录结构由格式固定，不受此策略影响。

<a id="migrate-a-journal-format"></a>

## 迁移日志格式

提供程序通过 <xref:Orleans.Durable.Journaling.IJournalMetadata.FormatKey> 和 <xref:Orleans.Durable.Journaling.JournalMetadata.FormatKey> 公开已持久化的格式键。恢复时独立于配置的写入格式选择已存储格式的读取器。当两种格式不同时，下一次写入会使用已配置的格式创建完整快照并更新元数据。<xref:Orleans.Durable.Journaling.Json.JsonLinesJournalFormat.JournalFormatKey> 提供 JSON Lines 的格式键。

使用以下部署顺序：

1. 将日志数据及提供程序元数据作为一个可恢复的整体进行备份。
1. 部署仍保留已存储格式的读取器和命令编解码器的二进制文件。
1. 在能够激活相应 Grain 类型的每个 Silo 上配置新的写入格式。
1. 使用有代表性的 Grain 进行验证，确认迁移压实成功。
1. 在整个回滚窗口内且直到已退役的状态流被移除为止，保留旧的读取器。

Orleans 二进制格式键为 `orleans-binary`。在维护现有二进制日志时显式配置该格式：

:::code language="csharp" source="./snippets/journaling/JournalingConfiguration.cs" id="configure_binary_format":::

已存储的格式键未知或有效载荷不兼容时，恢复会失败，日志保持不变。

<a id="configure-state-retirement"></a>

## 配置状态退役

从 Grain 中移除的命名状态在宽限期内仍可恢复：

:::code language="csharp" source="./snippets/journaling/JournalingConfiguration.cs" id="configure_retirement":::

默认最短期限为七天。期限到期后，通过压实操作将删除结果持久化。设置的期限应覆盖部署发布、观察和回滚过程。

<a id="development-storage"></a>

## 开发环境存储

<xref:Orleans.Durable.Journaling.JournalingHostingExtensions.AddJournaling*> 注册核心服务、格式、持久化状态工厂和生命周期集成。通过提供程序专用方法或通用的 <xref:Orleans.Durable.Journaling.JournalingHostingExtensions.AddJournalStorage*> 方法注册存储。运行时测试和可丢弃的开发主机可使用带有提供程序名称的 <xref:Orleans.Durable.Journaling.JournalingHostingExtensions.AddVolatileJournalStorage*>。其内容位于进程内存中，因此应使用持久化的模拟器存储来验证重启恢复及提供程序迁移。

<xref:Orleans.Durable.Journaling.VolatileJournalStorageOptions> 控制保留的追加历史记录。默认情况下，在 **成功追加 100 次或追加 1 MiB（1,048,576 字节）** 数据后请求生成快照，以先达到的限制为准：

:::code language="csharp" source="./snippets/journaling/JournalingConfiguration.cs" id="volatile_journal_thresholds":::

状态管理器在下次写入前观察到该请求。该次写入以当前状态的快照替换历史记录；成功替换后重置两个计数器。快照字节数不计入追加字节数。删除并重新创建日志时，从空历史记录开始。各限制必须为正数；单次追加可能超过字节阈值。

使用接受提供程序名称及选项配置委托的重载，配置彼此独立的命名阈值。提供程序在构造时获取配置的限制。计数器属于各个日志，由同一提供程序中指向同一日志的句柄共享。较大的限制将完整状态快照的复制开销摊销到更多更新；较小的限制减少保留的追加历史记录和重放工作量。应结合当前快照大小设定这两个限制。保留的内存包括最新快照、截至且包含触发超限的那次追加的所有已编码追加字节，以及存储提供程序的内部管理数据。

在预发布环境中采用与生产环境相同类别的持久化提供程序，以便在真实条件下验证恢复、压实、并发及备份流程。
