<a id="durable-jobs-journaling-playground"></a>

# 持久工作日记操场

Aspire 应用程序运行耐久工作流程 grains,一个网络载荷驱动程序,
Prometheus/Grafana监测。使用共享的
`Playground:Storage:Provider` 设置:

| 价值值值 | 日记储存 | 集群组合 |
| --- | --- | --- |
| `Azurite` (违约) | 紫色团 | 紫色表格 |
| `AzuriteTable` | 紫色表格 | 紫色表格 |
| `StandardBlob` | 标准 Azure 结团账户 | 标准帐户表服务 |
| `PremiumBlob` | Premium LRS BlockBlobStorage 账户账户 | 单独标准账户 |
| `Table` | Azure 表格 | 同一标准帐户的表服务 |
| `Azure` | `PremiumBlob` 别别  | 单独标准账户 |

双级Blob使用附加布洛布WAL和路障布洛检查站。
曝光布列资源; AppHost 删除不支持的队列/表格产出
通过单独的标准账户提供集群。

从仓库根根, 与 .NET SDK 和一个与多克兼容的容器
运行 AppHost。它使用 Aspire CLI 捆包
弦划; SDK 使用已安装的占位 CLI 或解决匹配
CLI 中 `dnx` 和配置 NuGet 资料来源:

```powershell
dotnet run --project playground\DurableJobsJournaling\DurableJobsJournaling.AppHost

# To select Table journaling on the emulator:
$env:Playground__Storage__Provider = 'AzuriteTable'
dotnet run --project playground\DurableJobsJournaling\DurableJobsJournaling.AppHost
```

AppHost 将正统后端传给 silo 和参考文件 `blobs` 或
日记数据的`journals` ,加上组群的 `tables` 。网络客户端使用
组合引用。每个运行的应用程序都生成一个基于 GUID 的容器
(`durablejobs-...`或日记表(`durablejobs...`)共有 silo
组合布局仍然 `wal/{journalId}` 和
`checkpoints/{journalId}/{snapshotId}`, 能够与
服务供应商默认的布局 。

每运行一次,登记两个有名的日记质集束 `jobs-a` 和 `jobs-b`。
名称解决独立集装箱(`durablejobs-...` 和
`durablejobs-b-...`)或表格(`durablejobs...` 和 `durablejobsb...`)。
默认情况下 A 是写A 是一个写入提供者, B 被选中用于排水、 准备
每个 silo 在B收到工作前发现任何一个命名空间。

具体资源专用于保存供视察的期刊。
数据量,这样存储就能够活过过程和 AppHost 重新启动。
Aspire 环境配置中的容器/表格名称,删除
当运行数据不再需要时, 运行中的数据不再需要资源。 要重新恢复/ 恢复
试验,使共享资源名称和工作量身份在
正在重新启动进程。

<a id="exercise-a-provider-cutover"></a>

## 实行供应商切换

使用
[持久就业移徙抽样](../../samples/DurableJobsMigration/README.md).
这个操场是用来进行交互式负载和故障实验的
进程- 本地工作流程 grain 状态驱动用户界面的状态状态驱动。使用样本中一直存在
用于核实恢复活动的布洛布收据。

使用稳定、小写字母数字运行标识符(最多40个字符)
在 AppHost 重新启动时重复使用两个命名空间:

```powershell
$env:Playground__Storage__RunId = 'migration01'
$env:Playground__Migration__ReportInventory = 'true'
$env:Playground__Migration__ActiveProviderName = 'jobs-a'
dotnet run --project playground\DurableJobsJournaling\DurableJobsJournaling.AppHost
```

使用负载控制,停止工作时间表、停止应用时间列表
排 AppHost 保持其 Azurite 的音量并稳定运行标识符 。
B 重新启动创建新的碎片和 A 仍然选择用于排水 :

```powershell
$env:Playground__Migration__ActiveProviderName = 'jobs-b'
dotnet run --project playground\DurableJobsJournaling\DurableJobsJournaling.AppHost
```

`DrainOtherProvider` 默认为 `true`。 启动时将固定提供方的选择;
重新启动 silo 以应用修改来调整其约束。
对于多silo 滚动实验,首先更新每个时间表 silo ,同时更新
选择了提供方, 然后开关写。 A- 配置 silos 可以继续创建
碎片在展出和飞行日程安排完成之前被碎片碎片碎片碎片碎片。
严格削减边界

`ReportInventory=true`, silo 日志报告每个登记册的完整目录清单
每30秒选定供应商,每个供应商有20秒超时。
这种选择入诊断增加了目录流量。 它报告总计, 拥有,
未经确认的碎片数加上最古老/最新的已知碎片
开始时间。总计包含未确认的条目 。
检查失败被登录为 **UNKNOWN**。该报告员只使用读
目录和元数据操作,并将结果写入本地 silo 日志。

保留 A 的读/ write/ 删除权限, 直到其完整目录为空,
包括未来和毒毒的作品, 所有A作家都停止了,
成功成功。退休要求成功的完全零库存加独立
证明已完成了整个集群的作者切换。
一旦这些条件维持下来,将重新开始
只选择 B 的`Playground__Migration__DrainOtherProvider=false` 。
保存 A 的命名空间, 直到它的排水管被验证。
以 A 重新启用为写提供商和 B 排气; B 中保留现有的 B 工作。

云后端选择可启用 Aspire Azure 的资源配置, 并可以
使用明确核准的 Azure 开发
及其标准航空安全认证/部署程序。
`PremiumBlob` 配置 `BlockBlobStorage` , `Premium_LRS`;标准账户
使用 `Standard_LRS`.
存储角色默认值符合选定的服务: Blob 和 表数据
共用标准分类组合/集群账户(表数据编制者)
表格期刊,以及单独的Blob单行的杂志和仅表群集
通用清单出版保留角色模块
部署的预期 `principalId` 和 `principalType` 投入
身份特性。 身份意识的快速部署环境可以应用这些默认值
之前配置该环境和身份
部署; 本地 Azurite 使用模拟证书。
使用相同的计算、区域、账户冗余、任务等公式比较后端
分布, 以及 `Playground:DurableJobs` 设置 。

从 Aspire 仪表板打开网络资源, 以配置负载通算/ 速率 。
开始和停止负荷,排出未完成的工作,检查工作流程的衡量标准。
现有阶段写和端至端的延时度量度、耐用工作再研究政策,
慢开始, 计划调试会应用到每个后端。 使用
调整这些调试的`Playground__DurableJobs__...` 设置。

仅供供应商提供有限工作量,孤立地清理资源,业务
百分百分位数,以及 JSON/CSV报告,使用
[Azure 日记账基准](../../test/Benchmarks/Journaling/Azure/README.md).
