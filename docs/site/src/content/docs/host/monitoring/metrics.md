---
title: "监测 Orleans 指标"
description: "解释当前 Orleans 客户和 silo 指标、仪器类型、单位、尺寸和健康指标。"
ms.date: 08/15/2026
ms.topic: concept-article
---

<a id="monitor-orleans-metrics"></a>

# 监测 Orleans 指标

Orleans 排放的量 <xref:System.Diagnostics.Metrics.Meter> 命名 `Microsoft.Orleans`。本页的仪器名称和语义描述当前运行时的安装过程。发现部署的仪器 Orleans 版本版本,因为释放和提供者的软件包可能不同。

```dotnetcli
dotnet-counters monitor -n <ProcessName> --counters Microsoft.Orleans
```

以下为 .NET 仪器名称。出口商可以使后端的仪器名称正常化,例如用下划线替换连字符。请参见当前每件文书及其源代码描述的 [完整 Orleans 指标目录](metrics-catalog.md) 。要检查正在开发中的定义,请参见 [InstrumentNames.cs](https://github.com/dotnet/orleans/blob/main/src/Orleans.Core/Diagnostics/Metrics/InstrumentNames.cs) 和相邻的工具执行。

<a id="interpret-instrument-types"></a>

## 解释性仪器仪器类型

仪器类型决定了哪个查询有意义。 不要在检查仪器类型和出口者时间性之前对每个显示的“ 当前”值保持警惕 。

| 仪器类型 | Orleans 使用 | 将它解释为 |
|---|---|---|
| 反或可观测柜台 | 已完成事件、故障和字节 | 单数增加一个进程寿命期的总和。 询问窗口的增速或速率。 当程序重新启动时, 请计算重置 。 |
| 上下计数台 | 连接客户端和 grain 实例 | 能够增减的运行中的总量。 当问题“现在有多少存在”时, 询问最新的总和值, 而不是其速率 。 |
| 观察测量表 | 连接的网关、启动、工作成员以及可用存储 | 时间点观察。总和:至少至少发现一个不健康的例子,而总金额则给出一组数的总数。 |
| 直方图 | 存储、激活、禁用、信息大小和提醒-感应性分布 | 分布观测结果。使用体积和桶数的计算,或尾巴行为百分位数的计算。配置包含预期值的后端桶边框。 |

OpenTelemetry 可以用累积或三角时间性来运输总金额。由老式遥测出口商显示的“当前”和“Delta”字段对一个计数器而不是单独的 Orleans 仪器进行了查看。目前的 OpenTelemetry 指南同样区分单体积、表和直方图,允许 [时间转换和重新合并](https://opentelemetry.io/docs/specs/otel/metrics/data-model/)。

表格中的单位描述每个仪器的自然单位。 Orleans 并不为每个仪器设定单位元数据,因此,即使出口商没有收到单位字符串, `count` 也可以是天然单位。如果排放单位元数据,有些既定工具使用历史字符串,如 `ms`、 `seconds`、 `bytes`和 `MB`,而目前的 [OpenTelemetry 通用语义公约](https://opentelemetry.io/docs/specs/semconv/general/metrics/) 一般建议UCUM单位,例如 `s` 和 `By`。在储存中保存所释放的单位并在查询中明确转换,而不是从计量名称中推断出一个单位。

<a id="client-signals"></a>

## 客户信号

健康 silo 无法补偿没有途径进入该组群的客户。

独立客户和 silos 何时发放 grain 电话

| 文书文书文书文书 | 类型和单位 | 意义和解释 |
|---|---|---|
| `orleans-client-connected-gateways` | 观察测量仪,计数 | 此客户端所持有的网关连接数量。 当它仍然为零时, 提醒您不要超过预期的重新连接窗口。 低于零的减少表示路径冗余较少, 并且应该与网关和网络信号相关联 。 |
| `orleans-app-requests-latency` | 直方图, `ms` | 发送被调用者观察到的完整外出请求回馈的分布。使用请求量的直方图计数、其总和和平均延时计数,以及尾巴行为中的桶或百分位数。 Orleans 不设定桶底线, 因为适当的延迟阈值取决于申请。 配置它们时使用精确名称的出口视图 。 |
| `orleans-app-requests-timedout` | 反对,计数; `grain_type` | 要求超过设定的反应超时时间; 提醒注意数量与已完成请求的比例,而不是一次超时或整个生命周期。 |
| `orleans-app-requests-canceled` | 反对,计数; `grain_type` | 被其呼叫者取消的请求。 使用应用程序背景和跟踪, 单独预计服务失败后会取消 。 |

呼叫员观察的请求延迟时间包括回召完成之前的时间, 以便可以包括运输、 排队、 grain 执行、 存储和响应发送。 它还记录了超时、 取消、 目标 -silo 失败以及主机关闭等终端路径 。 使用跟踪和 [事件运行记录](troubleshooting.md) 来定位延迟 。

直方图仪器之前的版本释放了三个可观测计数器,名称分别为 `orleans-app-requests-latency-bucket`、 `orleans-app-requests-latency-count`和 `orleans-app-requests-latency-sum`。桶流使用了 `duration` 属性和相互排斥的带子。字形图用标准直方图分布和浮点数取代这些流。出口商选择运输代表;例如,Prometheus经典直方图使用累积 `le` 桶系列。更新出口者的观点、仪表板、警报、记录规则以及升级时的查询。

根据应用程序的潜长目标和预期长尾尾尾尾端配置 。 在分布被合并的每个实例上使用相同的边界 。 例如, 一个通用 毫秒配置文件可以使用 `1`, `5`, `10`, `25`, `50`, `100`, `250`, `500`, `1000`, `2500`, `5000`, `10000`, `30000`,和 `60000`,并辅之以具体应用的《标准、水平和指标》阈值。 Orleans 默认。

<a id="silo-signals"></a>

## Silo 信号

<a id="admission-messaging-and-membership"></a>

### 接纳、信息传递和成员

| 文书文书文书文书 | 类型和单位 | 意义和解释 |
|---|---|---|
| `orleans-gateway-connected-clients` | 上下计数 | 合并后, 此网关上的当前客户连接。 网关间突然损失可以显示推出或网络中断; 持续扭曲可显示路径不均匀 。 |
| `orleans-gateway-load-shedding` | 计数对计 | 请求被拒绝,因为网关超载探测器报告饱和。任何持续的非零率都表示用户能看见的接收压力。 |
| `orleans-messaging-rejected` | 反对,计数; `Direction` | 拒绝信件。 将速度与负载折叠、 请求超时、 CPU、 内存和依赖性延缓值相匹配 。 |
| `orleans-messaging-sent-failed` | 反对,计数; `Direction` | 无法发送的信件。 以实例和方向与套接字和远程(silo ) 日志相对应 。 |
| `orleans-messaging-sent-dropped` | 反对,计数; `Direction` | 信息在运行时故意丢弃,持续增加需要记录和追踪调查。 |
| `orleans-messaging-expired` | 反对,计数; `Phase` | 在 `Send`、 `Receive`、 `Dispatch`、 `Invoke`或 `Respond`期间过期的邮件。该阶段缩小了执行前或执行过程中发生的延误。 |
| `orleans-messaging-pings-reply-missed` | 反对,计数; `Destination` | 直接或间接会员资格的检测失败。 警告持续增加或未达到的回复率,并与会籍过渡相关。 在暂停、 重新开始或短暂的包损失中,可能会出现孤立的缺失。 |
| `orleans-networking-sockets-opened` / `orleans-networking-sockets-closed` | 计数对计; `Direction` | 连接点。 比较它们的费率, 并与发送失败相关。 它们的差异只在一个过程寿命期内才有意义 。 |

信件大小仪器 `orleans-messaging-sent-messages-size` 和 `orleans-messaging-received-messages-size` 是 `bytes`中的直方图。使用其分布值进行有效载荷增长,并将其总数用于带宽诊断。它们包括 `ConnectionDirection`、 `MessageDirection`,有时还包括一个远程 `silo` 属性。

<a id="scheduling-and-activations"></a>

### 日程安排和启动

| 文书文书文书文书 | 类型和单位 | 意义和解释 |
|---|---|---|
| `orleans-scheduler-long-running-turns` | 计数对计 | Grain 同步执行超过的微圆 <xref:Orleans.Configuration.SchedulingOptions.TurnWarningLengthThreshold>默认值为一秒钟。寿命期总数自然只会增加;提醒注意其比率,并与延迟、CPU、线性资源库、跟踪和警告日志相关。 |
| `orleans-catalog-activations` | 观察测量仪,计数; `grain_type` | 启动目录 silo中登记的目标,包括系统目标和无国籍者-工人群体背景。在记忆和交通方面对每种类型进行跟踪,以识别增长、不平衡或杂交。 |
| `orleans-catalog-activation-working-set` | 观察测量仪,计数; `grain_type` | 最近积极启动的活性活动,保留到工作定型驱逐或停止使用。 |
| `orleans-grains` | 上下计数台,数; `grain_type` | 在案例分配和处置三角洲汇总后,构建了 grain 实例,每个无国籍工人案例各自作出贡献。 |
| `orleans-catalog-activation-created` / `orleans-catalog-activation-destroyed` | 计数对计; `grain_type` | 数据创建和未注册的传输量。双向的高比率表示即使目前的激活测量仪平坦,也会出现曲折。 |
| `orleans-catalog-activation-latency` | 直方图, `ms`; `grain_type`, `status`, `directory` | 激活期和结果。 分解非成功状态 (`canceled`、 `directory_error`、 `duplicate`或 `error`),并与目录和储存健康相关联的尾部延迟。 |
| `orleans-catalog-deactivation-latency` | 直方图, `ms`; `grain_type`, `via` | 使用 grain 类型和关闭路径来诊断失效期限。 使用它来诊断收集、 迁移和关闭延误 。 |
| `orleans-catalog-activation-failed-to-activate` | 反对,计数; `grain_type` | 启动过程周期启动失败或被取消的激活尝试。 持续增加是应用程序可用性信号。 |

`grain_type` 值是(包括明确名称和构建的通用参数在内的)语句 `GrainId.Type.ToString()` 身份。 Orleans 将这一数值隐藏在每登记类型中一次,并在激活过程中共享。目标事件使用该类型在 grain ID中;批量活动保留其批量范围。人口边界和汇总见 [Grain- 类型识别和聚合](metrics-catalog.md#grain-type-identity-and-aggregation) 。

对于生命周期完成量,总事件对应器在不同类型和任何未加标记的捐款之间增加在同一间隔内。对于目前的组群人口,对最新的每个silo 计数值进行总和,包括无标记的工作定值成员。配置出口资源身份和后端新鲜规则,以便每个 silo 都贡献一个当前人口。使用相同的观测窗口和新政策来比较每个接收人群。

长途转弯意味着 Orleans 观察到一个预定的工作项目同步执行,超出配置的警告阈值。 它本身并不证明是僵局。 常见原因包括同步阻塞、锁争吵、CPU重力工作或屏蔽 I/O。 见 [Grain 转弯似乎卡住了](troubleshooting.md#grain-turns-appear-stuck)。

<a id="storage"></a>

### 储存

| 文书文书文书文书 | 类型和单位 | 意义和解释 |
|---|---|---|
| `orleans-storage-read-latency` | 直方图, `ms` | 成功完成 grain- 状态读取缓存 。 |
| `orleans-storage-write-latency` | 直方图, `ms` | 成功完成 grain- 状态写缓存。 |
| `orleans-storage-clear-latency` | 直方图, `ms` | 成功完成 grain- 声明明确的延迟 。 |
| `orleans-storage-read-errors` | 计数对计 | 读取失败 。 |
| `orleans-storage-write-errors` | 计数对计 | 写作失败。 |
| `orleans-storage-clear-errors` | 计数对计 | 明显失败 。 |

所有存储工具都使用 `provider_type_name`、 `state_name`和 `state_type`。直方图数是成功操作量。对于窗口错误率,将误差率的增加除以相匹配的直方图值增加和误差率增加的总和。百分比表示平均隐藏的尾部延时。比较 Orleans 与提供者自身的振动、容量和服务度量之间的长度。

<a id="runtime-resources-and-watchdog"></a>

### 运行资源和监督

| 文书文书文书文书 | 类型和单位 | 意义和解释 |
|---|---|---|
| `orleans-runtime-available-memory` | `MB` 观测测量仪  | 数值是使用每份报告的甲基溴1,0242字节计算的。 提醒注意与总预算的比率并与工作规定、GC堆肥、分配率和集装箱限额相关联。 |
| `orleans-runtime-total-physical-memory` | `MB` 观测测量仪  | 尽管有历史名称,但执行情况报告 <xref:System.GC.GetGCMemoryInfo> <xref:System.GCMemoryInfo.TotalAvailableMemoryBytes>,其中可反映一个配置的或容器内存限值,而不是实际存储记录和档案管理。 |
| `orleans-watchdog-health-checks` | 计数对计 | 进行了运行时监察检查。 |
| `orleans-watchdog-health-checks-failed` | 计数对计 | 检测失败时间检查。 提醒您在确认检查继续运行的同时, 提醒您注意失败 。 |

收集 [内置 `System.Runtime` 度](https://learn.microsoft.com/dotnet/core/diagnostics/built-in-metrics-runtime) 和 Orleans。至少, 监测进程CPU的时间、 工作设定、 分配率、 GC型积大小和按生成顺序的碎裂情况、 GC 暂停时间、 线索组合队列长度和线条计数、 锁定参数、 例外计数 和套接字计数。 当与受限制运行时资源相关时, 不断上升的 Orleans 关卡信号可以被激活 。

<a id="dimensions-and-cardinality"></a>

## 层面和基本

计量身份包括资源属性和工具属性。设定稳定的资源属性,如 `service.name`、 `service.version`、 `service.instance.id`和 `deployment.environment.name`。只有在遥测数据政策允许并约束值时,才添加组群、区域和部署环身份。

实用 Orleans 维度是受约束的作业类别:

- `grain_type` 用于金字形 grain 和类型以及类别 `type` 系统目标类型。
- `Direction`、 `ConnectionDirection`、 `MessageDirection`和 `Phase`。
- 生命周期结果的`status`、 `directory`和 `via` 。
- `provider_type_name`、 `state_name`和 `state_type` ,用于配置的储存。

使用 `Destination` 和 `silo`。它们的值包括 silo 地址和内装,因此滚动重新启动和不稳定的会籍创建新的序列。保留它们进行短期诊断或使用 OpenTelemetry 视图或收集器处理器从长期总量中去除它们。不要添加 grain 密钥、请求或追踪身份、用户或租户身份、例外信息或其他未锁定的数值作为衡量属性。

分析每个重要信号,包括每例和整个组别:

- **最小的每个实例** 发现一个断开的客户端或可用内存的 silo 低。
- **每一个实例** 的上限发现热 silo、提供商路径缓慢或职位分配不平衡。
- **群集总和或比率** 衡量总需求、错误和能力。
- **部署与配置的转换比较** 查明推出期间的回归。

<a id="build-alerts-from-symptoms"></a>

## 建立症状警报

一生中的比例、比率和持续窗口超过总寿命:

- 连网关为零、请求超时/错误比率违反服务目标或维持网关负荷延缓。
- 错漏的探测点与会籍丢失或冗余减少相吻合,而不是一个错误的答复。
- 存储错误率或尾部延迟时间导致启动或请求失败的页面。
- 当可用的模拟主会议室几乎用完或GC暂停时,正与停滞不前的进展同时进行。
- 在激活增长、 CPU、 排队或长途转弯到超载断线点之前, 创建能力警告 。
- 道路孤立故障、每个grain类型不平衡,以及仪表板或票的逐步资源趋势,除非用户产生影响。

设定正常部署和负载测试基线的阈值,而不是复制通用数字值。

调查序列见 [排除故障 Orleans 事件](troubleshooting.md)。关于启动、准备状态、生命状态和依赖性健康,见 [健康和可观察性](../../deployment/health-and-observability.md)。
