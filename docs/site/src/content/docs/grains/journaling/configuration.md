---
title: "配置 Orleans 日报"
description: "选择实验 Orleans 日报存储提供者和日记格式。"
ms.date: 10/19/2026
ms.topic: how-to
---

<a id="configure-orleans-journaling"></a>

# 配置 Orleans 日报

配置由 grains 或使用 <xref:Orleans.Durable.Journaling.IDurableStateManager> 上每 silo 这等人,是那等人的主主; grain 类型。命名的提供者给 grain 和独立杂志的用户独立存储命名空间。 Grain 类型类型,没有明确的选择用途 `Default`提供商登记还增加了核心日报服务、耐久状态工厂和关键服务、启动生命周期整合, JSON 格式,和 Orleans 二进制阅读器 。

`Microsoft.Orleans.Durable` 及其储存提供者包件是释放前的阿尔法包件。

<a id="configure-grain-scoped-composition"></a>

## 配置 grain-范围构成

标准 <xref:Orleans.Durable.Journaling.JournalingHostingExtensions.AddJournaling*> 登记用品 `IDurableStateManager` 使用现有的激活服务范围,每启动一次。 grain 与激活装置一起建造的寿命周期 <xref:Orleans.Runtime.IGrainContext>。将该经理人注入普通 <xref:Orleans.Grain>,一个应用程序拥有的 grain 或一个激活范围范围的特性。 `GetOrAdd` 施工期间或同步启动装置安装过程中的辅助手或键基注射或键基注射。 <xref:Orleans.Runtime.GrainLifecycleStage.SetupState> 之前完成 <xref:Orleans.Grain.OnActivateAsync*> 以及请求。

储存登记使整个国家都能够提供这种服务。 silo. 与grain 由直接解决管理者问题的启动或通过持久性状态依赖引发的日记读写。 grain 活化保留了它们现有的持久性行为。

<xref:Orleans.Durable.Journaling.DurableGrain> provides optional protected helpers for the same scoped manager. The default manager implements both `IDurableStateManager` and the independent <xref:Orleans.Durable.Journaling.IJournaledStateManager> contract. Custom grain manager replacements provide both contracts and their registrations as appropriate, and establish lifecycle enrollment in their constructor or registration factory before resolution returns. Reusable features compose their own lifecycle work through a [shared activation setup action](../grain-lifecycle.md#shared-activation-setup).

<xref:Orleans.Durable.Journaling.IJournaledStateManagerFactory.CreateStandalone*> 返回明确供应的国营机器部件的日记账所有人合同。调用者构建和登记部件,初始化日记并处置所有者。组件依赖性及其寿命仍为调用者所有。独立日记业主不使用经理拥有的DI范围。

源支持的建筑师注入和设置特征示例见 [使用耐久状态](durable-state.md) ,生命周期订单见 [激活和恢复](runtime-behavior.md#activation-and-recovery) 。

<a id="choose-a-storage-provider"></a>

## 选择存储提供者

| 提供商 | 套包 | 储存模型 | 断裂触发器 | 最大的附件 |
| --- | --- | --- | --- | --- |
| [Azure 布线存储](azure-storage.md#azure-blob-storage) | `Microsoft.Orleans.Durable.AzureStorage` | 附加布洛布加上不可更改的检查站布洛布 | 追加布列块预算 | 100 MiB |
| [Azure 表格储存](azure-storage.md#azure-table-storage) | `Microsoft.Orleans.Durable.AzureStorage` | 每个带有页头和定序数据行的日记一个分区 | 列数或日记字节 | 2 MiB |
| [Redis](redis-journal-storage.md) | `Microsoft.Orleans.Durable.Redis` | 字符串日记加散列元数据 | 月刊 | 服务器字符串限制范围内的整批日记(默认情况下为512 MB) |
| 亚马逊 S3 | `Microsoft.Orleans.Durable.S3` | WAL 物体加上不可变检查站物体 | 追加预算编次 | 5 GB |

每个供应商都撰写每个附件和替换全部或无,而且没有写出超过其整个限度的文字;见 [日记仓储合同](runtime-behavior.md#journal-storage-contract)。根据原子文字限制、重播延时长、耐久配置、备份工具、最大热度grain 大小以及操作熟悉程度选择一个供应商。

<a id="select-named-providers"></a>

## 选择命名的提供者

Blob、表、 Redis、S3和易挥发的注册API接受供应商名称。
每一个命名的登记都约束其选项、储存、目录和州管理者
将一个物理日记命名空间的外加设备安装为。
`Default` 绑定。其他名称下的登记使这一缺省保持不变。

对于命名后端登记,既提供供应商名称,又提供选项
。当选项被单独配置时,通过 `configure: null` 。

`Default` 存储绑绑保留未命名后端选项配置
包括便利之前或之后登记的备选代表
和每个 `ConfigureAll` 代表使用过一次的方法。
使用相应的命名后端选项。
因此,存储绑定名和后端选项名称是不同的
默认约束。

一种无锁单键单调 <xref:Orleans.Durable.Journaling.IJournalStorageProvider> 自定义用品
用于 grain 激活和 `Default` 键入工厂的存储。默认
使用该供应商,因此替换供应商
执行注册供应商类型申报的目录和生命周期合同。

提供者名识别存储,而不是日记格式或个人
状态。将每个选定的物理命名空间登记一次并保留
账户、集装箱、表格、桶、主要前缀和命名功能
里面有工作,还有工作

配置在 `Orleans:Journaling:{name}`下的每个日记存储绑定 。
`ProviderType` 选择其存储提供者, `ServiceKey` 选择其 Azure
或从依赖性注射中 Redis 客户端。 Orleans 通过每个条目的名称,
向注册的供应商创建器配置区域。使用 `Default` 用于 grain
带有默认选择的选项类型,以及选定存储装订的其他名称。

例如,这些条目配置默认日记和归档
已单独注册的 Redis 客户端和密钥前缀:

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

归档客户端的等效环境变量密钥是
`Orleans__Journaling__archive__ServiceKey`.

<a id="select-storage-per-grain-type"></a>

### 选择每个 grain 类型的存储

应用 <xref:Orleans.Durable.Journaling.JournalStorageProviderAttribute> 到 grain 类
选择已注册、正则、对大小写敏感的服务提供人名称。 Orleans 发布
中选择 grain-类型元数据,并在创建激活时解决
继承的属性提供基级人员的选择;
类型选择的派生 grain 类供应上的属性。

登记在每张 silo 主机主机 grain
例如,两个名为 Azure 的Blob供应商可以使用单独的容器
与默认容器并列:

:::code language="csharp" source="./snippets/journaling/GrainJournalProviders.cs" id="configure_grain_providers":::

<xref:Orleans.Durable.Journaling.DurableGrain> 辅助工和建筑师注射使用
选中的提供者 :

:::code language="csharp" source="./snippets/journaling/GrainJournalProviders.cs" id="select_grain_providers":::

所有注入的耐久价值、收集和日记支持
<xref:Orleans.Runtime.IPersistentState`1> 共享共享, 激活的 管理器并写入
。 提供商在激活寿命期内保持固定状态。 无效或
未注册的选择失败, 配置错误会显示 grain 类型
以及提供商在访问日记数据前。

使用 grain现有日记读取回收时读取选定的物理命名空间
身份。更改 grain 类型选择,将新的启动指向
对于现有期刊的 grain 类型,计划有意的数据迁移
切开或切换:停止写入旧命名空间, 转移和校验日记
在整个 silos统一部署新的选择。
滚回计划中的提供者选择和命名空间。

寻找持久工作, <xref:Orleans.Hosting.DurableJobsExtensions.UseJournaledDurableJobs*>
选择日记执行。
<xref:Orleans.Hosting.DurableJobsOptions.ActiveProviderName> selects the provider
对于新碎片和默认值 `Default`。
<xref:Orleans.Hosting.DurableJobsOptions.DrainingProviderNames> selects additional
包含现有碎片的供应商,默认为空。
装订物要求储存、目录和工厂服务,并在
装订在该工艺的寿命期内保持不变。

被指定选择的,见 [移徙持久就业储存](durable-jobs-migration.md)
部署中转,以及全称空间退休检查。
根据该指南链接的该指南,根据由此建立的包件来练习新的API
仓库。日记片段项目中引用当前存储器
来源,因此它们的配置示例在当前检出中使用了 APIs。

<a id="configure-the-json-format"></a>

## 配置 JSON 格式

JSON 行是默认的写格式。 <xref:Orleans.Durable.Journaling.Json.JsonJournalHostingExtensions.UseJsonJournalFormat*> 登记源生成元数据,用于用作耐用键、值、收集项目、持久性状态或耐用任务结果的应用类型:

:::code language="csharp" source="./snippets/journaling/JournalingConfiguration.cs" id="configure_json_format":::

格式格式释放 UTF-8 JSON 每行有一个完整日记条目的行 `application/jsonl` 提供商支持内容类型的元数据。

序列式系统命名政策影响应用有效载荷值,日记指挥名和记录结构仍按格式固定。

<a id="migrate-a-journal-format"></a>

## 移动日记格式

供应商将持续格式键作为 <xref:Orleans.Durable.Journaling.IJournalMetadata.FormatKey> 和 <xref:Orleans.Durable.Journaling.JournalMetadata.FormatKey>来显示。回收后选择存储的读者,而不考虑配置的写法格式。如果它们不同,下一个写入会使用配置的格式创建完整的快照并更新元数据。 <xref:Orleans.Durable.Journaling.Json.JsonLinesJournalFormat.JournalFormatKey> 提供 JSON 行格式键。

使用此部署序列 :

1. 将日记数据和供应商元数据作为可回收单位备份。
1. 部署双元文件,保留存储格式的读数和命令编码。
1. 在每个 silo 类型上配置新的写格式,以激活 grain 类型。
1. 演习代表 grains 并确认迁移压实成功。
1. 保留前一位读者, 直至退休状态流被移除。

Orleans 二进制格式键是 `orleans-binary`。在保留现有二进制日记的同时, 明确配置它:

:::code language="csharp" source="./snippets/journaling/JournalingConfiguration.cs" id="configure_binary_format":::

未知的存储格式密钥或不兼容的有效载荷无法回收,日记不变。

<a id="configure-state-retirement"></a>

## 配置国家退休

从 grain 消失的点名状态在宽限期内仍可恢复:

:::code language="csharp" source="./snippets/journaling/JournalingConfiguration.cs" id="configure_retirement":::

默认最低时间为七天。 在期限结束后, 清除会持续压缩。 设定时间覆盖部署展开、 观察和回滚 。

<a id="development-storage"></a>

## 发展储存

<xref:Orleans.Durable.Journaling.JournalingHostingExtensions.AddJournaling*> 登记核心服务、格式、耐久状态工厂和生命周期整合。通过特定供应商的方法或通用 <xref:Orleans.Durable.Journaling.JournalingHostingExtensions.AddJournalStorage*> 方法进行注册存储。运行时间测试和可支配开发主机可以使用供应商名称的 <xref:Orleans.Durable.Journaling.JournalingHostingExtensions.AddVolatileJournalStorage*> 。其内容包含在过程内,因此使用持久性模拟器存储来验证恢复和提供者迁移的情况。

<xref:Orleans.Durable.Journaling.VolatileJournalStorageOptions> controls the retained append
历史历史历史。默认要求在 **100个成功附件或1个 MiB之后快速简况
(1,048,576字节)** 所附数据,以先达到的限度为准:

:::code language="csharp" source="./snippets/journaling/JournalingConfiguration.cs" id="volatile_journal_thresholds":::

国家经理在下次写下之前注意到这一要求。
历史, 并用当前状态的快照; 成功替换重置
。 抓图字节从附加的字节数中排除。 删除和
娱乐从空闲历史开始。 每个限制必须是正值; 单附件
超过字节阈值。

使用超过超载来接受提供者名称和选项代表配置
独立命名的阈值。当
已经构建的对数器属于每个日记,由
在一个服务供应商内部为同一日记处理器。
更新更多更新后全状态快照副本;减少保留
附加历史和重放工作。 在当前快照旁的大小限制都相等 。
保存的内存包括最新的快照,并附有编码字节,直至和
包括过境附件和存储供应商的簿记。

利用同样的耐久供应商类别进行生产,生产使用这种类别进行回收、压实、同值货币和备用程序,得到现实的验证。
