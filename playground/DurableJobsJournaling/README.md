<a id="durable-jobs-journaling-playground"></a>

# 持久化作业日志化实验项目

Aspire 应用运行持久化工作流 Grain、Web 负载驱动程序，以及
Prometheus/Grafana 监控。使用共享
`Playground:Storage:Provider` 设置选择日志存储：

| 值 | 日志存储 | 集群 |
| --- | --- | --- |
| `Azurite`（默认） | Azurite Blob | Azurite Table |
| `AzuriteTable` | Azurite Table | Azurite Table |
| `StandardBlob` | 标准 Azure Blob 账户 | 标准账户上的 Table 服务 |
| `PremiumBlob` | 高级 LRS BlockBlobStorage 账户 | 独立标准账户的 Table 服务 |
| `Table` | Azure Table | 同一标准账户上的 Table 服务 |
| `Azure` | `PremiumBlob` 的别名 | 独立标准账户的 Table 服务 |

两种 Blob 层级都使用追加 Blob WAL 和块 Blob 检查点。高级账户
公开 Blob 资源；AppHost 移除不受支持的 Queue/Table 输出，并
通过独立标准账户提供集群服务。

在具有 .NET SDK 和 Docker 兼容容器
运行时的环境下，从仓库根目录运行 AppHost。它使用 Aspire CLI 套件进行
编排；SDK 使用已安装的 Aspire CLI，或通过 `dnx` 及配置的 NuGet 包源解析与之匹配的
CLI：

```powershell
dotnet run --project playground\DurableJobsJournaling\DurableJobsJournaling.AppHost

# To select Table journaling on the emulator:
$env:Playground__Storage__Provider = 'AzuriteTable'
dotnet run --project playground\DurableJobsJournaling\DurableJobsJournaling.AppHost
```

AppHost 将规范化的后端传给 Silo，并引用 `blobs` 或
`journals` 作为日志数据资源，以及 `tables` 作为集群资源。Web 客户端使用
集群引用。每次应用运行生成基于 GUID 的容器
（`durablejobs-...`）或日志表（`durablejobs...`），由该次运行的各 Silo
副本共享。Blob 布局保持为 `wal/{journalId}` 和
`checkpoints/{journalId}/{snapshotId}`，支持通过提供程序的
默认布局进行目录发现。

每次运行注册两个具名日志绑定：`jobs-a` 和 `jobs-b`。
这些名称解析为独立的容器（`durablejobs-...` 和
`durablejobs-b-...`）或表（`durablejobs...` 和 `durablejobsb...`）。
默认 A 为写入提供程序，B 被选中用于排空，使
每个 Silo 在 B 接收工作前即可发现任一命名空间。

本次运行专用资源保留日志供检查。Azurite 使用
数据卷，使存储跨进程和 AppHost 重启保留。从 Aspire 环境配置记录
容器或表名称，并在不再需要本次运行数据时删除该
资源。对于重启或恢复
实验，应在重启的各进程间保持共享资源名称和工作负载标识
稳定不变。

<a id="exercise-a-provider-cutover"></a>

## 演练提供程序切换

若需要具有明确成功或失败检查的最小确定性控制台场景，请使用
[Durable Jobs 迁移示例](../../samples/DurableJobsMigration/README.md)。
本实验项目用于交互式负载及故障实验，其 UI 由
进程局部的工作流 Grain 状态驱动。使用示例持久化的
Blob 回执验证重启恢复。

使用稳定的小写字母数字运行标识符（最多 40 个字符），以便在 AppHost 重启之间
复用两个命名空间：

```powershell
$env:Playground__Storage__RunId = 'migration01'
$env:Playground__Migration__ReportInventory = 'true'
$env:Playground__Migration__ActiveProviderName = 'jobs-a'
dotnet run --project playground\DurableJobsJournaling\DurableJobsJournaling.AppHost
```

调度工作，使用负载控制停止应用程序调度，并优雅停止
AppHost。保留其 Azurite 数据卷及稳定运行标识符。
重启并让 B 创建新分片，A 仍被选中用于排空：

```powershell
$env:Playground__Migration__ActiveProviderName = 'jobs-b'
dotnet run --project playground\DurableJobsJournaling\DurableJobsJournaling.AppHost
```

`DrainOtherProvider` 默认为 `true`。提供程序选择在启动时确定；
重启 Silo 才能应用绑定变更。
对于多 Silo 滚动实验，首先更新每个调度 Silo，同时选择两个
提供程序，随后切换写入。配置为 A 的 Silo 可能继续创建
A 分片，直至其滚动更新及正在进行的调度结束。若需严格切换边界，应暂停
调度。

设置 `ReportInventory=true` 时，Silo 日志每 30 秒报告每个
选定提供程序的完整目录清单，每个提供程序超时为 20 秒。
这一需显式启用的诊断会增加目录流量。它报告分片总数、已有所有者的
分片数、有毒分片数及无法识别的分片数，以及最旧和最新的已识别分片
开始时间。总数包括无法识别的条目。
检查失败记录为 **UNKNOWN**。报告器使用只读
目录及元数据操作，并将结果写入本地 Silo 日志。

保留 A 的读写及删除权限，直至其完整清单为空，
包括未来工作及有毒工作，所有 A 写入者均已停止，且清理已经
成功。退役要求成功的全量清单零计数，以及独立
证据证明全群集写入者切换已完成。
满足这些条件后，使用
`Playground__Migration__DrainOtherProvider=false` 重启，仅选择 B。
在验证 A 已排空之前保留其命名空间。若需回滚，
重启并配置 A 作为写入提供程序、B 用于排空；现有 B 作业保留在 B 中。

选择云后端会启用 Aspire 的 Azure 资源配置，并可能
预配产生费用的资源。使用经明确批准的 Azure 开发
环境及其标准 Aspire 身份验证和部署流程。
`PremiumBlob` 将 `BlockBlobStorage` 配置为 `Premium_LRS`；标准账户
使用 `Standard_LRS`。
默认存储角色与所选服务相匹配：共享的标准 Blob/集群账户使用 Blob 和 Table 数据
参与者角色，Table 日志使用 Table 数据参与者角色，
高级 Blob 使用独立的仅 Blob 日志角色和仅 Table 集群
角色分配。通用清单发布保留角色模块，使用 `principalId` 和 `principalType` 输入指定部署目标
标识。
支持标识的 Aspire 部署环境可以将这些默认设置应用于
其应用程序标识。部署之前配置该环境及标识；
本地 Azurite 使用模拟器凭据。
比较后端时应采用相同的计算资源、区域、账户冗余、作业
分布及 `Playground:DurableJobs` 设置。

从 Aspire 仪表板打开 Web 资源，配置负载并发度及速率，
启动和停止负载、排空未完成工作，并检查工作流指标。
现有的阶段写入及端到端延迟指标、持久化作业重试策略、
慢启动及调度调优适用于所有后端。使用
`Playground__DurableJobs__...` 设置调整这些调优参数。

若需具有隔离资源清理、操作
百分位数及 JSON/CSV 报告的有限提供程序专用工作负载，请使用
[Azure 日志基准测试](../../test/Benchmarks/Journaling/Azure/README.md)。
