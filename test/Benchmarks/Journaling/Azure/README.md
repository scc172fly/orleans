<a id="azure-journal-provider-benchmarks"></a>

# Azure 期刊提供商基准

`Journaling.Azure` 比较耐用附加件、检查站更换、字节恢复、
并经过 `IJournalStorage` 和 `IJournalStorageCatalog`。
`Journaling.Azure.Bdn` 在 BenchmarkDotNet下运行同样的操作。
[持久就业运动场](../../../../playground/DurableJobsJournaling/README.md)
练习 grain 和工作表的时长时间,与相同的后端选择。

<a id="backends-and-authentication"></a>

## 后端和认证

| 后端 | 储存实施 | 账户核查 |
| --- | --- | --- |
| `AzuriteBlob` | 附加布洛布瓦勒和路障布检查站 | 模拟器, 只回回 |
| `AzuriteTable` | 表 杂志头和数据代 | 模拟器, 只回回 |
| `StandardBlob` | 附加布洛布瓦勒和路障布检查站 | `GetAccountInfoAsync`:标准SKU和兼容账户类型 |
| `PremiumBlob` | 同一Blob公司在溢价账户上提供商 | `GetAccountInfoAsync`: `BlockBlobStorage` 和溢价SKU |
| `Table` | Azure 表格日记账存储 | 请求后端; 账户第一级未核查 |

溢价 **BlockBlobStorage 账户支持附加表 blobs**。账户的分层是
比较轴;两个Blob案件都执行相同的WAL和检查站代码。
独立运行器接受 `StandardBlob`的SDK SKUs标准支持标准 SDK SKUs
并记录 `AccountSku`中的实际SKU; `data-plane-verified` 描述
账户类型/ SKU 检查。 操作员在现有的账户中选择冗余 。
游乐场的用品与 `Standard_LRS` 和 `Premium_LRS` 预设的相匹配。

账户层比较时,将基准主机和账户置于同一位置
Azure 区域,匹配冗余设置,并保持计算、有效载荷、历史、
运行之间 共通货币和目录分布常数。不同的
冗余设置引入额外的比较尺寸。
与每份报告同时提交,并附有配置和服务方计量标准。

**实际 Azure 运行不发生存储、交易和可能的网络收费。**
需要 `--allow-azure true`使用现有的测试账户和 Entra 连接
数据贡献器或存储表数据贡献器,
包括创建和删除孤立容器/表格的许可。
`DefaultAzureCredential` 支持本地 Azure CLI 签名和管理身份。
通过环境变量配置服务端点 :

```powershell
# Sign in using your organization's normal Azure CLI procedure.
az login
$env:JOURNAL_BENCHMARK_BLOB_ENDPOINT = 'https://yourstandardaccount.blob.core.windows.net'
$env:JOURNAL_BENCHMARK_TABLE_ENDPOINT = 'https://yourtableaccount.table.core.windows.net'
```

Azure 端点使用 HTTPP 来 Entra 认证。 运行者接受端点
环境设置和出口仅来自后端、经核实的种类/ SKU,以及
例外报告保留阶段,例外
类型、 HTTP 状态、运行指数和基准验证代码。
值、连接字符串、 URI 查询和 SDK 例外信息
报表。在创建资源之前,账户信息权限或分层错配失败。

<a id="build-and-emulator-smoke"></a>

## 构建和模拟烟雾

从仓库根运行命令, 由 `global.json`选择 SDK 。

```powershell
dotnet build test\Benchmarks\Benchmarks.csproj -c Release -f net11.0

# In a separate terminal, start an installed Azurite on its default loopback ports.
azurite --skipApiVersionCheck --location .\.azurite-journal-bench

# In the benchmark terminal:
$run = Join-Path $env:TEMP ('journal-bench-' + [guid]::NewGuid().ToString('N'))
dotnet run --project test\Benchmarks\Benchmarks.csproj -c Release -f net11.0 --no-build -- `
  Journaling.Azure --backend AzuriteBlob --workload DurableAppend --output $run
```

`UseDevelopmentStorage=true` 提供默认模拟器连接。
设置为单独配置的回回回回回路模拟器
`JOURNAL_BENCHMARK_AZURITE_CONNECTION_STRING` 在环境中。所有问题都解决了
模拟服务端点必须是回溯地址 。

以启用元数据的方式,实施两个执行和五个操作:

```powershell
foreach ($backend in 'AzuriteBlob', 'AzuriteTable') {
  foreach ($workload in 'DurableAppend', 'CheckpointReplace', 'RecoveryReplay', 'CatalogBounded', 'CatalogUnbounded') {
    $run = Join-Path $env:TEMP ("$backend-$workload-" + [guid]::NewGuid().ToString('N'))
    dotnet run --project test\Benchmarks\Benchmarks.csproj -c Release -f net11.0 --no-build -- `
      Journaling.Azure --backend $backend --workload $workload --operations 3 --concurrency 2 `
      --due-journals 3 --future-journals 8 --metadata true --output $run
    if ($LASTEXITCODE -ne 0) { throw "Benchmark failed; inspect $run.json and console output." }
  }
}
```

重复 `--metadata false` ,比较预测和存储的元数据间接费用。
模拟结果确定正确性和当地基准。 Azure
计算和储存环境的预期 Azure 的账户性能。

<a id="fixed-work-comparisons-and-scale"></a>

## 固定工作比较和比额表

每种情况都选择一个后端和工作量。

```powershell
$run = Join-Path $env:TEMP ('standard-append-' + [guid]::NewGuid().ToString('N'))
dotnet run --project test\Benchmarks\Benchmarks.csproj -c Release -f net11.0 --no-build -- `
  Journaling.Azure --backend StandardBlob --allow-azure true --workload DurableAppend `
  --operations 100 --concurrency 8 --payload-bytes 1024 --batch-size 8 `
  --history-batches 16 --checkpoint-bytes 65536 --metadata true --output $run
```

用于比较保险费,在保险费上点 `JOURNAL_BENCHMARK_BLOB_ENDPOINT`
BlockBlobStorage 账户并选择 `PremiumBlob`。
`JOURNAL_BENCHMARK_TABLE_ENDPOINT` 和选择 `Table`。
保持全部配置, 与每个
增加业务和共同货币供应
饱和工作量:每个工人在上一个工作之后开始其下一个业务
完成一个

| 选项 | 默认默认 | 含义/硬限 |
| --- | --- | --- |
| `--operations` | 8 | 计量业务共计,1-10 000 |
| `--concurrency` | 2 | 固定工,最多为固定工,最多为业务和256 |
| `--payload-bytes` | 256 | 每一固定长有效载荷记录字节数 |
| `--batch-size` | 4 | 每个附件的记录; 最多2 MiB 有效载荷字节的产品  |
| `--history-batches` | 4 | 在每个种子检查站后各组附加的批次,0-10 000 |
| `--checkpoint-bytes` | 4096 | 完整替换有效载荷,1 字节-32 MiB |
| `--due-journals` | 16 | 分类表应包含范围, 0- 100 000 身份 |
| `--future-journals` | 64 | 未来尾尾巴目录, 0- 100 000 身份 |
| `--metadata` | 假 | 存储两个呼叫器元数据属性和项目目录元数据 |
| `--seed` | 42 | 确定性有效载荷种子 |
| `--max-work` | 100,000 | 估计逻辑/设置/跨轨道工作上限,至多10 000 000 |
| `--max-bytes` | 268,435,456 | 估计有效载荷/元数据处理上限,至多8个 GiB |
| `--setup-timeout-seconds` | 120 | 设置+种子+热播+种子+暖化的最后期限;还有单独的核查期限 |
| `--timeout-seconds` | 60 | 整个计量截止日期 |
| `--cleanup-timeout-seconds` | 60 | 独立清理截止日期 |
| `--output` | azure-journal-results | 新 JSON 和 CSV 文件的前缀; 父目录必须存在 |

估计工作包括暖和核查。
工作与有效载荷处理; Azure SDK重整和存储电线
客户最多使用两个SDK重写和10秒
在所附阶段截止期限内完成网络超时。

<a id="operation-contracts"></a>

## 业务合同

设置创建了确定性缓冲、新资源以及之前的基准
测量。每个突变都有独立的日记和一个作家。
日记从一个检查站和所要求的附加历史开始。
添加日记的用品保温。 它给每个测量的附加件或
取代相同的初始状态,并限制小和小的升温。

| 工作量 | 一次测量操作 | 核查 |
| --- | --- | --- |
| `DurableAppend` | 等待一个原子 `AppendAsync` 批次承诺 | 测量检查检查站+历史+附加批次后的新手 |
| `CheckpointReplace` | 等待一个 `ReplaceAsync`,包括默认的过时发电清理 | 仅读取替换的检查 |
| `RecoveryReplay` | 创建一个新的存储器,并消耗完整的检查站/WAL字节流 | 每个块块都消耗;在操作中检查每个块的消耗量;在操作中检查订购字节、字节数、SHA-256、格式和 `IsCompleted` |
| `CatalogBounded` | 充分列举前缀和包容性的自有/自有下限/上限 | 成员、计算、独特性和要求的元数据 |
| `CatalogUnbounded` | 完全列举相同的前缀/下下边,上框开放 | 确切的、+未来成员、计数、独特性和要求提供的元数据 |

存储格式登记为 `benchmark-bytes-v1`
`application/octet-stream`。它的投入包括确定性固定长度
字节记录:这是衡量提供商 I/O+验证;
`Journaling` 发送系统供应了记忆中的耐久国家代码基准。
回收读回回调保证了统一格式;完整呼叫元数据
利用外部存储元数据 API 进行测试,对ETags进行检查。
列表操作中检查目录元数据。

分类表设置还创建了下限以下的过期身份,并创建了
前缀外的无关身份。 将未来尾尾巴提升到5,000以上
组合项或1,000个表格项进行服务呼叫。
成员制和成员制检查确保基准计量标准检索和跨行。
设置空的应设区域 `--due-journals 0` , 未来尾尾尾或同时设定
空选定命名空间的目录计为零。

<a id="reports-and-units"></a>

## 报告和单位

Schema 2型 JSON 包含由倒数拥有的作业测量和
单项/重试计数器:

| 外地 | 意义的含义含义 |
| --- | --- |
| `Configuration` | 后台、工作量、种子、规模、工作上限、要求的货币/计算、最后期限和选择适用 |
| `Build` | 上载基准、日记 Azure 存储组装名称、源代码修改和模块版本ID |
| `Resource`, `Ownership`, `Cleanup` | 产生资源特性和生命周期成果 |
| `AccountVerification`, `AccountKind`, `AccountSku` | 模拟器、经数据机核查的布层层或要求的未核实表格 |
| `Completed`, `Failed`, `Cancelled`, `NotStarted` | 相互排排独性结果总结到所要求的操作量 |
| `Operations` | 毫秒的运行 `Stopwatch` 延迟度、成功的有效载荷字节和项目 |
| `SuccessfulLatency`, `FailedLatency`, `CancelledLatency` | 算术平均数,计算计算法中,接近于p50/p95/p99,按结果分别列出 |
| `ElapsedSeconds`, `CompletedOperationsPerSecond` | 测量窗口和完成的计算除以该窗口 |
| `CompletedPayloadBytes`, `PayloadBytesPerSecond` | 成功承诺或重放调用字节和字节/窗口秒;目录贡献为零 |
| `CompletedItems`, `ItemUnit` | 附加记录、替换、检查站+重放记录或目录条目 |
| `Failures`, `Phase`, `Success` | 部分结果故障诊断和全运行结果,包括核查/清理 |
| `ProviderMetrics` | 供应商准确的DI `Microsoft.Orleans` 表上的目录页、候选项目、交付条目和明确重写 |

附加的八种记录是 **1** 时间化操作。
恢复和目录延迟期包括全部
设置、暖和、写后
核查、清理和出口的期内、输送量和供应方之外
以小样本计数得出粗缩百分点;增加
用于尾部延时分析的操作。
固定工作跑步器在开始启动时激活其供应商的通用听众
当工人完成劳动,在核查之前和之后,
计量固定工程的一部分是计量回回回回和汇总。
配置。

CSV有一个摘要行,即变化式文化数字值、引用字段和
JSON中的相同配置、原动操作、故障和供应商的衡量标准
列。使用 `Import-Csv` 读取它,用 `ConvertFrom-Json` 读取嵌套的列。
下 JSON 包括运行时间/操作系统以及明确的源/单位说明。 `Build`
(也以CSV `build_json`身份导出)
使用其编译器生成 `ModuleVersionId` 和十六进制的组件
.NET SDK 所附的源版本订正本附于其信息版本。
`SourceRevision` 联 年 月 日 月 日 月 月 日 `null` 明确丢失或未经确认的修订记录
元数据元数据模块ID 区分了汇编的代码,包括本地变化
在同一修订中保留带有报告并准确复制的二进制文件。
这个来源来自装货组装 所以经营旧建筑
更改了结账后仍然有报告显示身份。
输出文件由创建新写作保存;导出失败产生
输出和控制台诊断

四台 `orleans-journaling-provider-` 柜台描述供应商语义:

| 后缀 | 计数 |
| --- | --- |
| `catalog-pages` | 在目录列表中,包括空页面在内的目录列表中成功收到的网页 |
| `catalog-items` | 候选人项目在全部网页中,在当地过滤之前 |
| `catalog-entries` | 实际向目录消费者上交的条目 |
| `retries` | 明确提供商的分机,按理由分类 |

每长行每长一行内 `Instrument`, `Unit` (`count`), `Provider`, `Reason`,
`Observations`和 `Sum`。 `Sum` 是一个整数对数总计; `Observations` 是
`Provider` 保留了供应商提供的
标签,并且 `Reason` 被重新输入反对数。已完成的目录扫描
获得的候选人比其产量多,但接受的候选人数量却多于所取得。
运行时可有一个空 `ProviderMetrics` 数组,当它们不执行
提供自控重试。 它们运行的计算、 结果、 有效载荷字节和延时
百分位数来自选手自己的样本

使用主机自有的 Azure SDK诊断、喷射集成或其他应用
请求计数、期限、结果和运输尝试的仪器。
与 Azure 服务度量和配置的SDK
重试和上传行为。目录页面/项目反面可以测量跨行工作。

Schema 2版确定了单行长的单体型和语义学。
历史史原图1第1版报告保留了这些仪器并进行了量度构建
使用其记录的方法和单位比较报告。

<a id="bounded-benchmarkdotnet-adapter"></a>

## 环形 BenchmarkDotNet 适配器

默认参数选择两个模拟器后端、256字节有效载荷,以及
五个工作量:十个案件。每个循环都设置新的资源,并进行执行
并用同步生命周期进行核查和清理
工作变异器实施一次热和三次测量迭代,
包括 CLI 工具链/工作选择取代默认监测任务时。
`Dry` 烟雾预设也保留了三地封隔的上限。
操作是一个供应商的呼叫或完整的跨行。
执行前,基准验证人检查最后解决的任务:1-3
测量的迭代,0-1热迭代, 一个调用, 释放系数 1,
和一次进程发射。它拒绝这些限制之外的配置
设置资源资源,包括适应性迭代计数。使用固定工作跑者
用于更大规模、更明确的比较。
其平均值描述迭代样本;使用固定工作跑者的独立
用于请求-详细分析的定时操作分布。
国内基本发展网路径使供应商的标准化收集工作停止,因此其时间包括
工作量和正确性检查,不采用固定工作跑者标准
收听回调或汇总。
每个基准进程在`Azure 基准中发布相同的构建信息
构建: '全球设置期间的 JSON 行。将 BDN 日志保留在配置的下
`--artifacts` 特别是为
多重时间或孤立过程比较。

```powershell
# Correctness-only smoke uses the already built assembly in process.
dotnet run --project test\Benchmarks\Benchmarks.csproj -c Release -f net11.0 --no-build -- `
  Journaling.Azure.Bdn --filter '*AzureJournalBenchmarks*' --job Dry --inProcess `
  --noOverwrite --artifacts "$env:TEMP\journal-bdn-smoke" > "$env:TEMP\journal-bdn-smoke.log" 2>&1
```

使用正常的孤立过程工具链进行比较(缩制 `--inProcess`);
允许为此工程的依附图允许 足够 `--buildTimeout` 秒 :

```powershell
# Run on the intended benchmark host with an existing standard Azure account.
$env:JOURNAL_BENCHMARK_BLOB_ENDPOINT = 'https://yourstandardaccount.blob.core.windows.net'
$env:JOURNAL_BENCHMARK_ALLOW_AZURE = 'true'
$env:JOURNAL_BENCHMARK_BDN_BACKENDS = 'StandardBlob'
$env:JOURNAL_BENCHMARK_BDN_PAYLOAD_BYTES = '256,4096'
dotnet run --project test\Benchmarks\Benchmarks.csproj -c Release -f net11.0 --no-build -- `
  Journaling.Azure.Bdn --filter '*AzureJournalBenchmarks*DurableAppend*' --buildTimeout 600 `
  --noOverwrite --artifacts "$env:TEMP\journal-bdn-standard" > "$env:TEMP\journal-bdn-standard.log" 2>&1
```

使用 `JOURNAL_BENCHMARK_BDN_BACKENDS` 选择后端(最多以comma分隔)
使用 `JOURNAL_BENCHMARK_BDN_PAYLOAD_BYTES` (最多3个)的大小。
`JOURNAL_BENCHMARK_BDN_OPTIONS` 接受空间分隔固定运行器选项
历史、检查站、目录、元数据和截止时间的校正
将 `JOURNAL_BENCHMARK_ALLOW_AZURE=true` 用品
国内基本需求选择使用显性云;终点仍位于相同的环境设置中。
使用筛选器选择一个工作量,而只选择相关的后端/大小值
故意保持案件统计和工作服务。

<a id="resource-ownership-and-failures"></a>

## 资源所有权和失败

每运行一次, 使用服务创建 GUID `journalbench...` 集装箱/表格
**在启动供应商生命周期之前创建** 操作。碰撞失败
设置并保留现有资源。清理仅删除
在所有工人加入后, 此运行确认创建 。 Ctrl+C 取消
设置/测量/核查,清理工作保留自己的有限期限。
作业失败停止进一步安排,加入在职工人和出口
部分结果,不零退出。
清理工作以相反顺序,包括
启动失败或取消的阶段, 在删除自有资源前
处置服务提供者。

如果创建响应丢失, `creation-unconfirmed` 和
`manual-check-required` 确定检查资源。
需要检查:它可能是指先前存在的资源或SDK在
成功创建此运行的创建响应失败了。 无论是结果失败设置还是效果失败
保存资源直到所有权确定。清理错误保留
操作者后续跟踪的资源名称和 `failed` 结果。
终止时还可以留下运行资源;使用生成的身份进行检查
并在确认所有权后删除该单项测试资源。
创建前将资源身份打印为标准错误
即使当程序在报告出口前停止时, 也可用 。
