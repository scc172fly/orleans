<a id="azure-journal-provider-benchmarks"></a>

# Azure 日志存储提供程序基准测试

`Journaling.Azure` 通过 `IJournalStorage` 和 `IJournalStorageCatalog` 比较持久化追加、检查点替换、字节恢复和目录遍历。
`Journaling.Azure.Bdn` 在 BenchmarkDotNet 下执行相同操作。[持久化作业实验项目](../../../../playground/DurableJobsJournaling/README.md) 使用相同的后端选项，测试 Grain 调度和工作流延迟。

<a id="backends-and-authentication"></a>

## 后端与身份验证

| 后端 | 存储实现 | 账户验证 |
| --- | --- | --- |
| `AzuriteBlob` | 追加 Blob 的 WAL 和块 Blob 检查点 | 模拟器，仅限环回地址 |
| `AzuriteTable` | Table 日志头及数据世代 | 模拟器，仅限环回地址 |
| `StandardBlob` | 追加 Blob 的 WAL 和块 Blob 检查点 | `GetAccountInfoAsync`：标准 SKU 和兼容的账户类型 |
| `PremiumBlob` | 在高级账户上运行相同的 Blob 提供程序 | `GetAccountInfoAsync`：`BlockBlobStorage` 和高级 SKU |
| `Table` | Azure Table 日志存储 | 按请求指定的后端；未验证账户层级 |

高级 **BlockBlobStorage 账户支持追加 Blob**。比较维度是账户层级；两种 Blob 情况执行相同的 WAL 与检查点代码。独立运行器接受 `StandardBlob` 支持的标准 SDK SKU，并在 `AccountSku` 中记录实际 SKU；`data-plane-verified` 表示已执行账户类型及 SKU 检查。操作员在已有账户中选择冗余配置。实验项目预配相互匹配的 `Standard_LRS` 和 `Premium_LRS` 预设。

比较账户层级时，将基准测试主机和账户置于同一 Azure 区域，匹配冗余设置，并在各次运行之间保持计算资源、有效载荷、历史记录、并发度及目录分布不变。不同冗余设置会引入额外的比较维度。请随每份报告记录账户配置及服务端指标。

**使用实际 Azure 运行会产生存储、事务以及可能的网络费用。**
必须指定 `--allow-azure true`。使用已有测试账户，以及具备 Storage Blob Data Contributor 或 Storage Table Data Contributor 角色的 Entra 标识（包括创建与删除隔离容器或表的权限）。`DefaultAzureCredential` 支持本地 Azure CLI 登录与托管标识。通过环境变量配置服务终结点：

```powershell
# Sign in using your organization's normal Azure CLI procedure.
az login
$env:JOURNAL_BENCHMARK_BLOB_ENDPOINT = 'https://yourstandardaccount.blob.core.windows.net'
$env:JOURNAL_BENCHMARK_TABLE_ENDPOINT = 'https://yourtableaccount.table.core.windows.net'
```

Azure 终结点通过 HTTPS 使用 Entra 身份验证。运行器从环境变量接收终结点设置，仅导出后端、经过验证的账户类型/SKU 和生成的资源标识。异常报告保留阶段、异常类型、HTTP 状态、操作索引及基准验证代码。凭据值、连接字符串、URI 查询参数以及 SDK 异常消息不会写入报告。账户信息权限或层级不匹配会在创建资源之前导致失败。

<a id="build-and-emulator-smoke"></a>

## 构建与模拟器冒烟测试

使用 `global.json` 选定的 SDK，从仓库根目录运行命令。

```powershell
dotnet build test\Benchmarks\Benchmarks.csproj -c Release -f net11.0

# In a separate terminal, start an installed Azurite on its default loopback ports.
azurite --skipApiVersionCheck --location .\.azurite-journal-bench

# In the benchmark terminal:
$run = Join-Path $env:TEMP ('journal-bench-' + [guid]::NewGuid().ToString('N'))
dotnet run --project test\Benchmarks\Benchmarks.csproj -c Release -f net11.0 --no-build -- `
  Journaling.Azure --backend AzuriteBlob --workload DurableAppend --output $run
```

`UseDevelopmentStorage=true` 提供默认的模拟器连接。
如果单独配置了环回模拟器，请在环境中设置 `JOURNAL_BENCHMARK_AZURITE_CONNECTION_STRING`。所有解析得到的模拟器服务终结点都必须是环回地址。

启用元数据后，对两种实现及五种操作进行测试：

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

使用 `--metadata false` 重复测试，以比较投影和存储元数据的开销。模拟器结果用于验证正确性及建立本地基线。Azure 账户性能应在预期的 Azure 计算与存储环境中测量。

<a id="fixed-work-comparisons-and-scale"></a>

## 固定工作量比较与规模调整

每次调用选择一个后端及一种工作负载。例如：

```powershell
$run = Join-Path $env:TEMP ('standard-append-' + [guid]::NewGuid().ToString('N'))
dotnet run --project test\Benchmarks\Benchmarks.csproj -c Release -f net11.0 --no-build -- `
  Journaling.Azure --backend StandardBlob --allow-azure true --workload DurableAppend `
  --operations 100 --concurrency 8 --payload-bytes 1024 --batch-size 8 `
  --history-batches 16 --checkpoint-bytes 65536 --metadata true --output $run
```

进行高级账户比较时，将 `JOURNAL_BENCHMARK_BLOB_ENDPOINT` 指向高级 BlockBlobStorage 账户，并选择 `PremiumBlob`。对于 Table，设置 `JOURNAL_BENCHMARK_TABLE_ENDPOINT` 并选择 `Table`。在不同运行中分别改变并发度、批大小和历史记录，并随每次结果保留完整配置。增加操作数和并发度会形成有界闭环饱和工作负载：每个工作者在上一个操作完成后才启动下一个操作。

| 选项 | 默认值 | 含义 / 硬性上限 |
| --- | --- | --- |
| `--operations` | 8 | 测量的总操作数，1–10,000 |
| `--concurrency` | 2 | 固定工作者数量，不超过操作数和 256 |
| `--payload-bytes` | 256 | 每条固定长度有效载荷记录的字节数 |
| `--batch-size` | 4 | 每次追加的记录数；与有效载荷字节数的乘积最多为 2 MiB |
| `--history-batches` | 4 | 每个预置检查点后的追加批数，0–10,000 |
| `--checkpoint-bytes` | 4096 | 完整替换有效载荷，1 字节–32 MiB |
| `--due-journals` | 16 | 目录中已到期范围，0–100,000 个标识 |
| `--future-journals` | 64 | 目录中未来到期的尾部范围，0–100,000 个标识 |
| `--metadata` | false | 存储两个调用方元数据属性，并投影目录元数据 |
| `--seed` | 42 | 确定性有效载荷种子 |
| `--max-work` | 100,000 | 估算的逻辑、准备和遍历工作量上限，最多 10,000,000 |
| `--max-bytes` | 268,435,456 | 估算的有效载荷和元数据处理量上限，最多 8 GiB |
| `--setup-timeout-seconds` | 120 | 准备、填充初始数据、预热的截止时间；也用于单独的验证截止时间 |
| `--timeout-seconds` | 60 | 整个测量阶段的截止时间 |
| `--cleanup-timeout-seconds` | 60 | 独立的清理截止时间 |
| `--output` | azure-journal-results | 新建 JSON 和 CSV 文件的前缀；父目录必须存在 |

工作量估算包含预热及验证。它们限制所请求的逻辑工作量和有效载荷处理量；Azure SDK 重试及存储传输开销会增加服务端工作量。在所属阶段的截止时间内，客户端最多使用两次 SDK 重试及 10 秒网络超时。

<a id="operation-contracts"></a>

## 操作契约

准备阶段在测量前创建确定性缓冲区、新资源和基线。每个变更操作使用独立的日志及一个写入者。所有日志初始均包含一个检查点及请求的追加历史。另有一个日志供预热使用。这使每个测量的追加或替换操作拥有相同的初始状态，并限制 WAL 增长。

| 工作负载 | 一次测量操作 | 验证 |
| --- | --- | --- |
| `DurableAppend` | 等待一次原子的 `AppendAsync` 批提交完成 | 测量后使用新句柄读取，检查检查点、历史及追加批次 |
| `CheckpointReplace` | 等待一次 `ReplaceAsync` 完成，包括默认的过时世代清理 | 使用新句柄读取，确认仅包含替换内容 |
| `RecoveryReplay` | 创建新的存储句柄并消费完整的检查点/WAL 字节流 | 消费每个数据块；在操作内检查字节顺序、字节数、SHA-256、格式及 `IsCompleted` |
| `CatalogBounded` | 完整枚举指定前缀及包含端点的到期标识上下界 | 精确的到期成员集合、数量、唯一性和所请求的元数据 |
| `CatalogUnbounded` | 完整枚举相同前缀及下界，不设上界 | 精确的到期及未来到期成员集合、数量、唯一性和所请求的元数据 |

存储格式注册为 `benchmark-bytes-v1`，媒体类型为 `application/octet-stream`。其输入由确定性的固定长度字节记录组成。此处测量提供程序 I/O 与验证；已有的 `Journaling` 分派入口提供内存中持久化状态编解码器的基准测试。恢复读取回调保证格式一致；完整的调用方元数据及 ETag 通过存储元数据 API 在测量之外检查。目录元数据在列举操作中检查。

目录准备阶段还会创建一个低于下界的过期标识及一个位于前缀之外的无关标识。将未来到期尾部提高至超过 5,000 个 Blob 条目或 1,000 个 Table 条目，可测试服务分页。完整枚举与成员检查确保基准测试测量实际检索及遍历。使用 `--due-journals 0` 测试到期范围为空而存在未来到期尾部的情况，或将两个目录计数均设为零以测试空的选定命名空间。

<a id="reports-and-units"></a>

## 报告与单位

架构版本 2 的 JSON 包含运行器自身的操作测量及目录/重试计数器：

| 字段 | 含义 |
| --- | --- |
| `Configuration` | 后端、工作负载、种子、大小、工作量上限、请求的并发度与操作数、截止时间及主动启用选项 |
| `Build` | 已加载的基准测试、Journaling 和 Azure Storage 程序集的名称、源码修订版本及模块版本 ID |
| `Resource`, `Ownership`, `Cleanup` | 生成的资源标识及生命周期结果 |
| `AccountVerification`, `AccountKind`, `AccountSku` | 模拟器、通过数据平面验证的 Blob 层级，或按请求指定但未验证的 Table |
| `Completed`, `Failed`, `Cancelled`, `NotStarted` | 互斥的操作结果，其总数等于请求的操作数 |
| `Operations` | 索引、结果、每次操作的 `Stopwatch` 毫秒延迟、成功处理的有效载荷字节数和项目数 |
| `SuccessfulLatency`, `FailedLatency`, `CancelledLatency` | 按结果分别统计的数量、算术平均值、最近秩法 p50/p95/p99 |
| `ElapsedSeconds`, `CompletedOperationsPerSecond` | 测量窗口时长以及完成数量除以窗口时长 |
| `CompletedPayloadBytes`, `PayloadBytesPerSecond` | 成功提交或重放的调用方字节数，以及每测量窗口秒的字节数；目录操作贡献为零 |
| `CompletedItems`, `ItemUnit` | 追加记录、替换次数、检查点及重放记录，或目录条目 |
| `Failures`, `Phase`, `Success` | 部分结果的失败诊断及整个运行结果，包括验证和清理 |
| `ProviderMetrics` | 来自提供程序通过 DI 使用的同一个 `Microsoft.Orleans` Meter 实例 的目录页数、候选项数、已交付条目数及显式重试次数 |

包含八条记录的追加是**一次**计时操作。其 p99 表示八条记录批提交的 p99。恢复及目录延迟包含完整的流消费及正确性检查。准备、预热、写入后验证、清理及导出不计入延迟、吞吐量及提供程序指标的测量窗口。样本数较少时，百分位数粒度较粗；应增加操作数以分析尾部延迟。
固定工作量运行器在测量开始时启用提供程序指标监听器，并在工作者完成后、验证及清理之前解除监听。指标回调及聚合属于被测固定工作量配置的一部分。

CSV 包含一行摘要、固定区域性格式的数值、带引号的字段，并在 JSON 列中提供相同的配置、原始操作、失败及提供程序指标。使用 `Import-Csv` 读取 CSV，使用 `ConvertFrom-Json` 解析嵌套列。
JSON 包含运行时/操作系统信息及明确的来源和单位说明。`Build`（亦以 CSV 的 `build_json` 导出）通过编译器生成的 `ModuleVersionId`，以及 .NET SDK 附加在信息性版本中的十六进制源码修订标识，识别三个已加载程序集中的每一个。
`SourceRevision` 为 `null` 时，明确表示修订元数据缺失或无法识别。模块 ID 可区分编译后的代码，包括基于同一修订版本构建的本地改动；要精确复现，请随报告保留二进制文件。此来源信息来自已加载程序集，因此即使改变检出内容后运行旧构建，报告的仍是该旧构建的标识。以仅创建新文件的方式写入，确保不覆盖现有输出文件；导出失败会产生非零退出码及控制台诊断。

四个带 `orleans-journaling-provider-` 前缀的计数器描述提供程序语义：

| 后缀 | 计数 |
| --- | --- |
| `catalog-pages` | 列举目录时成功接收的页数，包括空页 |
| `catalog-items` | 这些完整页中本地筛选前的候选项数量 |
| `catalog-entries` | 实际向目录消费者产生的条目数 |
| `retries` | 提供程序显式重试次数，按原因分组 |

每行指标均包含 `Instrument`、`Unit`（`count`）、`Provider`、`Reason`、`Observations` 和 `Sum`。`Sum` 是整数计数器总量；`Observations` 是接收到的测量次数。`Provider` 保留提供程序提供的标签，重试计数器会填充 `Reason`。一次完整目录扫描收到的候选项可能多于实际产生的条目。追加、替换及恢复运行在未发生显式提供程序重试时，`ProviderMetrics` 数组可以为空。其操作数、结果、有效载荷字节数及延迟百分位数均来自运行器自身的样本。

使用主机管理的 Azure SDK 诊断、Aspire 集成或其他应用插桩获取请求数、时长、结果及传输尝试次数。结合 Azure 服务指标及所配置 SDK 的重试和上传行为进行成本核算。目录页数和候选项计数器测量遍历工作量。

架构版本 2 表示仅针对目录的指标行结构与语义。历史架构版本 1 的报告保留原始运行的度量工具与被测构建；比较报告时请依据其记录的架构版本和单位。

<a id="bounded-benchmarkdotnet-adapter"></a>

## 有界 BenchmarkDotNet 适配器

默认参数选择两个模拟器后端、256 字节有效载荷及五种工作负载，共十个测试用例。每次迭代准备新资源并执行一次受保护的调用，再使用同步生命周期桥接执行验证及清理。Job 修改器强制执行一次预热及三次测量迭代，即使 CLI 工具链或 Job 选择替换默认监控 Job 时也是如此。`Dry` 冒烟预设同样保留三次迭代上限。BDN 报告的操作是一次提供程序调用或完整遍历。
执行前，基准验证器检查最终解析的 Job：1–3 次测量迭代、0–1 次预热迭代、一次调用、展开因子为 1，且启动一个进程。它会在资源准备前拒绝超出这些限制的配置，包括自适应迭代次数。较大且明确指定规模的比较请使用固定工作量运行器。
其平均值描述的是迭代样本；分析请求尾部延迟时，应使用固定工作量运行器独立计时的操作分布。BDN 路径不会启用提供程序指标采集，因此其计时包含工作负载及正确性检查，但不包含固定工作量运行器的指标监听器回调及聚合。
每个基准测试进程在全局准备阶段以 `Azure benchmark
build:` JSON 行输出相同的构建信息。请将已配置的 `--artifacts` 目录下的 BDN 日志与摘要报告一同保留，尤其是在比较多个运行时或隔离进程时。

```powershell
# Correctness-only smoke uses the already built assembly in process.
dotnet run --project test\Benchmarks\Benchmarks.csproj -c Release -f net11.0 --no-build -- `
  Journaling.Azure.Bdn --filter '*AzureJournalBenchmarks*' --job Dry --inProcess `
  --noOverwrite --artifacts "$env:TEMP\journal-bdn-smoke" > "$env:TEMP\journal-bdn-smoke.log" 2>&1
```

进行比较时使用常规隔离进程工具链（省略 `--inProcess`）；根据项目依赖图为 `--buildTimeout` 提供足够的秒数：

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

使用 `JOURNAL_BENCHMARK_BDN_BACKENDS`（逗号分隔，最多五个）选择后端，使用 `JOURNAL_BENCHMARK_BDN_PAYLOAD_BYTES`（最多三个）选择大小。
`JOURNAL_BENCHMARK_BDN_OPTIONS` 接受以空格分隔的固定工作量运行器选项，包括历史、检查点、目录、元数据及截止时间。适配器将操作数和并发度固定为一。`JOURNAL_BENCHMARK_ALLOW_AZURE=true` 表示显式允许 BDN 使用云服务；终结点仍通过相同的环境设置提供。
使用筛选器选择一种工作负载及相关后端和大小，以有意控制用例数量和服务工作量。

<a id="resource-ownership-and-failures"></a>

## 资源所有权与失败

每次运行在启动提供程序生命周期之前，使用服务的 **create** 操作创建以 GUID 命名的 `journalbench...` 容器或表。发生冲突时准备阶段失败，并保留已有资源。所有工作者汇合后，清理仅删除已向本次运行确认创建成功的资源。Ctrl+C 取消准备、测量及验证；清理仍有自身的有限截止时间。
操作失败时停止进一步调度，等待活动工作者结束，并以非零退出码导出部分结果。清理在删除拥有的资源并释放服务提供程序之前，按相反顺序停止所有尝试启动的生命周期阶段，包括启动失败或被取消的阶段。

如果创建响应丢失，`creation-unconfirmed` 和 `manual-check-required` 会标识需要检查的资源。最终收到 HTTP 409 时同样需要检查：它可能表示已有资源，也可能表示本次运行创建成功的响应丢失后发生了 SDK 重试。两种结果都会使准备阶段失败，并在确定所有权前保留资源。清理错误会保留资源名称及 `failed` 结果，以供操作员后续处理。进程突然终止也可能遗留本次运行的资源；请使用生成的标识检查相应的单个测试资源，并在确认所有权后删除。创建前会将资源标识打印到标准错误流，因此即使进程在报告导出之前停止，也可以获得该标识。
