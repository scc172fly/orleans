---
title: 监控 Orleans 指标
description: 理解当前 Orleans 客户端和 Silo 指标、测量工具类型、单位、维度及健康指标。
ms.date: 08/15/2026
ms.topic: concept-article
---

<a id="monitor-orleans-metrics"></a>

# 监控 Orleans 指标

Orleans 通过名为 `Microsoft.Orleans` 的 <xref:System.Diagnostics.Metrics.Meter> 发出指标。本页的测量工具名称和语义描述当前运行时实现。不同发行版和提供程序包可能有所不同，因此请发现实际部署的 Orleans 版本所发出的测量工具。

```dotnetcli
dotnet-counters monitor -n <ProcessName> --counters Microsoft.Orleans
```

以下名称是 .NET 测量工具名称。导出器可以按照其后端要求对名称进行规范化，例如将连字符替换为下划线。有关当前全部测量工具及源代码中的描述，请参阅 [完整 Orleans 指标目录](metrics-catalog.md)。若要检查正在开发的定义，请参阅 [InstrumentNames.cs](https://github.com/dotnet/orleans/blob/main/src/Orleans.Core/Diagnostics/Metrics/InstrumentNames.cs) 及相邻的测量工具实现。

<a id="interpret-instrument-types"></a>

## 理解测量工具类型

测量工具类型决定何种查询有意义。在检查测量工具类型和导出器的时间聚合方式之前，不要对每个显示的“当前”值设置告警。

| 测量工具类型 | Orleans 用途 | 解释方式 |
|---|---|---|
| 计数器或可观测计数器 | 已完成事件、失败次数及字节数 | 单个进程生命周期内单调递增的总量。查询时间窗口内的增量或速率，并考虑进程重启时的重置。 |
| 增减计数器 | 已连接客户端及 Grain 实例 | 可以增加也可以减少的累计值。若问题是“当前存在多少”，应查询最新的聚合值，而不是变化速率。 |
| 可观测测量计 | 已连接网关、激活实例、工作集成员及可用内存 | 某一时刻的观测值。有意识地选择聚合方式：最小值通常可以发现不健康的实例，而计数求和可以得到集群总数。 |
| 直方图 | 存储、激活、停用、消息大小及提醒器延迟分布 | 观测值的分布。使用计数了解数量，使用桶或百分位数分析尾部行为。配置覆盖预期值范围的后端桶边界。 |

OpenTelemetry 可以使用累积或增量时间聚合方式传输求和值。较旧遥测导出器显示的“Current”和“Delta”字段是同一计数器的不同视图，而不是独立的 Orleans 测量工具。当前 OpenTelemetry 指南同样区分单调求和值、测量计和直方图，并允许 [时间聚合方式转换和重新聚合](https://opentelemetry.io/docs/specs/otel/metrics/data-model/)。

表格中的单位描述各测量工具的自然单位。Orleans 并不为每个测量工具设置单位元数据，因此即使导出器没有收到单位字符串，自然单位仍可以是 `count`。在发出单位元数据的情况下，一些既有测量工具使用 `ms`、`seconds`、`bytes` 和 `MB` 等历史字符串，而当前 [OpenTelemetry 指标语义约定](https://opentelemetry.io/docs/specs/semconv/general/metrics/) 通常推荐 `s` 和 `By` 等 UCUM 单位。在存储中保留实际发出的单位，并在查询中显式转换，而不是根据指标名称推断单位。

<a id="client-signals"></a>

## 客户端信号

从调用方的可用性和延迟视角开始排查。即使 Silo 健康，也无法弥补客户端没有到达集群的路由这一问题。

独立客户端和发起 Grain 调用的 Silo 均会发出调用方请求测量指标。

| 测量工具 | 类型及单位 | 含义和解释 |
|---|---|---|
| `orleans-client-connected-gateways` | 可观测测量计，计数 | 此客户端持有的网关连接数量。如果超过预期重连窗口后仍为零，则发出告警。数量下降但仍大于零表示路径冗余减少，应与网关及网络信号进行关联分析。 |
| `orleans-app-requests-latency` | 直方图，`ms` | 调用方观测到的已完成出站请求回调的延迟分布。使用直方图计数获取请求量，使用总和与计数计算平均延迟，使用桶或百分位数分析尾部行为。Orleans 不规定桶边界，因为合适的延迟阈值取决于应用程序。请使用按精确名称匹配的导出器 View 配置边界。 |
| `orleans-app-requests-timedout` | 计数器，计数；`grain_type` | 超过配置响应超时时间的请求。告警应依据满足最小请求量条件的超时与已完成请求之比，而不是单次超时或生命周期总量。 |
| `orleans-app-requests-canceled` | 计数器，计数；`grain_type` | 由调用方取消的请求。使用应用程序上下文和跟踪区分预期取消与服务故障。 |

调用方观测到的请求延迟包含直至回调完成的时间，因此可能包括传输、排队、Grain 执行、存储及响应交付。它还记录超时、取消、目标 Silo 故障及宿主关闭等终止路径。使用链路跟踪及 [故障运维手册](troubleshooting.md) 定位延迟。

在直方图测量工具出现之前的版本会发出三个名为 `orleans-app-requests-latency-bucket`、`orleans-app-requests-latency-count` 和 `orleans-app-requests-latency-sum` 的可观测计数器。桶数据流使用 `duration` 属性和互斥的区间。直方图用标准直方图分布及浮点求和值替代这些数据流。导出器选择传输表示形式；例如 Prometheus 经典直方图使用累积的 `le` 桶时间序列。升级时请更新导出器 View、仪表板、告警、记录规则及查询。

根据应用程序延迟目标和预期长尾配置边界。所有需要合并分布的实例都应使用相同的边界。例如，通用毫秒配置可以采用 `1`、`5`、`10`、`25`、`50`、`100`、`250`、`500`、`1000`、`2500`、`5000`、`10000`、`30000` 和 `60000`，再加入应用程序特定的 SLO 阈值。这只是示例，并非 Orleans 默认配置。

<a id="silo-signals"></a>

## Silo 信号

<a id="admission-messaging-and-membership"></a>

### 准入、消息传递和成员资格

| 测量工具 | 类型及单位 | 含义和解释 |
|---|---|---|
| `orleans-gateway-connected-clients` | 增减计数器，计数 | 聚合后此网关当前的客户端连接数量。多个网关突然失去连接可能表示滚动部署或网络中断；持续的分布偏斜可能表示路由不均衡。 |
| `orleans-gateway-load-shedding` | 计数器，计数 | 由于网关过载检测器报告饱和而被拒绝的请求。任何持续的非零速率都表示用户可感知的准入压力。 |
| `orleans-messaging-rejected` | 计数器，计数；`Direction` | 被拒绝的消息。将速率与负载削减、请求超时、CPU、内存及依赖项延迟进行关联分析。 |
| `orleans-messaging-sent-failed` | 计数器，计数；`Direction` | 无法发送的消息。按实例和方向，与套接字频繁变化及远程 Silo 日志进行关联分析。 |
| `orleans-messaging-sent-dropped` | 计数器，计数；`Direction` | 运行时有意丢弃的消息。持续增长需要通过日志和链路跟踪调查。 |
| `orleans-messaging-expired` | 计数器，计数；`Phase` | 在 `Send`、`Receive`、`Dispatch`、`Invoke` 或 `Respond` 阶段过期的消息。阶段可缩小排查范围，帮助判断延迟发生在执行之前还是执行期间。 |
| `orleans-messaging-pings-reply-missed` | 计数器，计数；`Destination` | 失败的直接或间接成员资格探测。对持续增长或探测响应缺失比例告警，并与成员资格状态转换关联。暂停、重启或暂时丢包时可能发生孤立的探测响应缺失。 |
| `orleans-networking-sockets-opened` / `orleans-networking-sockets-closed` | 计数器，计数；`Direction` | 连接频繁变化。比较两者速率并与发送失败关联。两者之差仅在同一进程生命周期内有意义。 |

消息大小测量工具 `orleans-messaging-sent-messages-size` 和 `orleans-messaging-received-messages-size` 是以 `bytes` 为单位的直方图。使用其分布分析有效载荷大小增长，使用总和诊断带宽问题。它们包含 `ConnectionDirection`、`MessageDirection`，有时还包含远程 `silo` 属性。

<a id="scheduling-and-activations"></a>

### 调度和激活实例

| 测量工具 | 类型及单位 | 含义和解释 |
|---|---|---|
| `orleans-scheduler-long-running-turns` | 计数器，计数 | 同步执行时间超过 <xref:Orleans.Configuration.SchedulingOptions.TurnWarningLengthThreshold> 的 Grain 微型执行轮次。默认阈值为一秒。生命周期总量自然只增不减；应对其速率告警，并与延迟、CPU、线程池、链路跟踪及警告日志关联。 |
| `orleans-catalog-activations` | 可观测测量计，计数；`grain_type` | 在此 Silo 激活实例目录中注册的目标，包括系统目标和无状态工作者组上下文。将各类型的趋势与内存和流量进行比较，识别增长、失衡或频繁创建及停用。 |
| `orleans-catalog-activation-working-set` | 可观测测量计，计数；`grain_type` | 最近处于活动状态的激活实例，保留到从工作集中逐出或停用为止。比较各类型的趋势与目录中的总体数量。 |
| `orleans-grains` | 增减计数器，计数；`grain_type` | 聚合实例分配及释放增量后已构造的 Grain 实例数量。每个无状态工作者实例单独计数。 |
| `orleans-catalog-activation-created` / `orleans-catalog-activation-destroyed` | 计数器，计数；`grain_type` | 激活实例目录中条目创建及注销的吞吐量。即使当前激活实例测量计数保持不变，两个方向的高变化速率也表示频繁创建及停用。 |
| `orleans-catalog-activation-latency` | 直方图，`ms`；`grain_type`、`status`、`directory` | 激活持续时间及结果。按非成功状态（`canceled`、`directory_error`、`duplicate` 或 `error`）细分，并将尾部延迟与目录及存储健康状况关联。 |
| `orleans-catalog-deactivation-latency` | 直方图，`ms`；`grain_type`、`via` | 按 Grain 类型及关闭路径记录的停用持续时间。用于诊断回收、迁移及关闭延迟。 |
| `orleans-catalog-activation-failed-to-activate` | 计数器，计数；`grain_type` | 生命周期初始化失败或被取消的激活尝试。持续增长是应用程序可用性信号。 |

`grain_type` 的值是规范的 `GrainId.Type.ToString()` 标识，包括显式名称和构造后的泛型实参。Orleans 为每个已注册类型缓存一次该值，并在激活实例之间共享。针对特定目标的事件使用其 Grain ID 中的类型；批次级事件保留其批次作用域。有关总体数量的统计边界及聚合，请参阅 [Grain 类型标识与聚合](metrics-catalog.md#grain-type-identity-and-aggregation)。

对于生命周期吞吐量，应在同一时间区间内对所有类型的事件计数器增量及任何无标签贡献求和。对于当前集群总体数量，应对各 Silo 最新且仍有效的测量计数求和，包括无标签工作集成员。配置导出器资源标识和后端数据新鲜度规则，使每个 Silo 只贡献一份当前总体数量。使用相同的观测窗口及新鲜度策略比较各宿主的总体数量。

长时间运行的执行轮次意味着 Orleans 观测到一个已调度工作项的同步执行超过配置的警告阈值。它本身并不能证明发生死锁。常见原因包括同步阻塞、锁争用、CPU 密集型工作或阻塞 I/O。请参阅 [Grain 执行轮次似乎卡住](troubleshooting.md#grain-turns-appear-stuck)。

<a id="storage"></a>

### 存储

| 测量工具 | 类型及单位 | 含义和解释 |
|---|---|---|
| `orleans-storage-read-latency` | 直方图，`ms` | 成功完成的 Grain 状态读取延迟。 |
| `orleans-storage-write-latency` | 直方图，`ms` | 成功完成的 Grain 状态写入延迟。 |
| `orleans-storage-clear-latency` | 直方图，`ms` | 成功完成的 Grain 状态清除延迟。 |
| `orleans-storage-read-errors` | 计数器，计数 | 读取失败。 |
| `orleans-storage-write-errors` | 计数器，计数 | 写入失败。 |
| `orleans-storage-clear-errors` | 计数器，计数 | 清除失败。 |

所有存储测量工具均使用 `provider_type_name`、`state_name` 和 `state_type`。直方图计数表示成功操作量。计算时间窗口内的错误比例时，将错误计数器增量除以相匹配的直方图计数增量与错误计数器增量之和。百分位数能够揭示平均值掩盖的尾部延迟。将 Orleans 延迟与提供程序自身的限流、容量及服务指标进行比较。

<a id="runtime-resources-and-watchdog"></a>

### 运行时资源和看门狗

| 测量工具 | 类型及单位 | 含义和解释 |
|---|---|---|
| `orleans-runtime-available-memory` | 可观测测量计，`MB` | GC 报告的可用内存预算。每个报告的 MB 按 1,024² 字节计算。对其占总预算的比例告警，并与工作集、GC 堆、分配速率及容器限制关联。 |
| `orleans-runtime-total-physical-memory` | 可观测测量计，`MB` | 尽管名称沿用历史命名，实现报告的是 <xref:System.GC.GetGCMemoryInfo> 的 <xref:System.GCMemoryInfo.TotalAvailableMemoryBytes>，可能反映配置的内存限制或容器内存限制，而非宿主物理内存。 |
| `orleans-watchdog-health-checks` | 计数器，计数 | 已执行的运行时看门狗健康检查。 |
| `orleans-watchdog-health-checks-failed` | 计数器，计数 | 失败的运行时看门狗健康检查。对失败告警，同时确认检查仍在持续执行。 |

在收集 Orleans 指标的同时收集 [内置 `System.Runtime` 指标](https://learn.microsoft.com/dotnet/core/diagnostics/built-in-metrics-runtime)。至少监控进程 CPU 时间、工作集、分配速率、按代统计的 GC 堆大小及碎片、GC 暂停时间、线程池队列长度和线程数量、锁争用、异常计数及套接字数量。将 Orleans 延迟上升信号与受限的运行时资源关联，才能采取有针对性的措施。

<a id="dimensions-and-cardinality"></a>

## 维度和基数

指标标识包含资源属性和测量工具属性。设置 `service.name`、`service.version`、`service.instance.id` 和 `deployment.environment.name` 等稳定的资源属性。仅当集群、区域和部署环标识的取值有界，且遥测数据策略允许时，才添加这些标识。

有用的 Orleans 维度是有界的运维类别：

- `grain_type` 表示规范 Grain 类型，`type` 表示系统目标类型。
- `Direction`、`ConnectionDirection`、`MessageDirection` 和 `Phase`。
- `status`、`directory` 和 `via` 表示生命周期结果。
- `provider_type_name`、`state_name` 和 `state_type` 表示已配置的存储。

谨慎使用 `Destination` 和 `silo`。它们的值包含 Silo 地址及实例代次，因此滚动重启和不稳定的成员资格会产生新的时间序列。将其保留用于短期诊断，或使用 OpenTelemetry View 或 Collector 处理器将其从长期聚合中移除。不要将 Grain 键、请求或跟踪 ID、用户或租户 ID、异常消息或其他无界值用作指标属性。

对每个重要信号，既按实例分析，也在集群范围内分析：

- **各实例中的最小值** 可找出断开连接的客户端或可用内存不足的 Silo。
- **各实例中的最大值** 可找出热点 Silo、缓慢的提供程序路径或放置失衡。
- **集群总和或速率** 衡量总需求、错误及容量。
- **部署版本比较** 识别滚动部署期间的回归问题。

<a id="build-alerts-from-symptoms"></a>

## 根据故障现象构建告警

优先使用比例、速率及持续时间窗口，而非生命周期总量：

- 当已连接网关持续为零、请求超时或错误比例违反服务目标，或者网关持续进行负载削减时，通知值班人员。
- 当探测响应缺失同时伴随成员资格丢失或冗余度下降时，通知值班人员，而不是对单次响应缺失通知。
- 当存储错误比例或尾部延迟导致激活或请求失败时，通知值班人员。
- 当可用内存余量接近耗尽，或 GC 暂停与进度停滞同时出现时，通知值班人员。
- 在激活实例增长、CPU、排队或长时间运行的执行轮次达到负载削减临界点之前，创建容量告警。
- 除非用户已经受到影响，否则将孤立故障、按 Grain 类型的失衡和逐步变化的资源趋势交给仪表板或工单处理。

为延迟及错误比例告警设置足够的最小请求量，以避免噪声百分比。根据正常部署及负载测试基线确定阈值，而不是照搬通用数值。

有关排查步骤，请参阅 [排查 Orleans 运行故障](troubleshooting.md)。有关启动、就绪、存活及依赖项健康状况，请参阅 [健康与可观测性](../../deployment/health-and-observability.md)。
