---
title: "Orleans 指标目录"
description: "Microsoft.Orleans 计数所排放的计量指标完整参考。"
ms.date: 08/18/2026
ms.topic: reference
---

<a id="orleans-metrics-catalog"></a>

# Orleans 指标目录

此目录列出了当前源树中为 `Microsoft.Orleans` 仪表创建的每个工具。 当组件运行时, 核心运行时间工具可用。 流、 交易、 耐久性任务、 日记和供应商专用仪器只有在这些特性配置时才出现。 发现已部署的 Orleans 版本所释放的工具, 因为发布和配置包可能不同 。

**单位** 栏给出了 Orleans记录的自然单位。“隐含”是指工具不设置单位元数据。 `TimeSpan` 计数为 100毫秒。 **属性** 列列表由 Orleans所添加的属性;资源属性,如 `service.instance.id` ,也标明每个时间序列。

使用这些缩略语的仪器类型:

- **C**: <xref:System.Diagnostics.Metrics.Counter`1>
- **OC(石油公司)**: <xref:System.Diagnostics.Metrics.ObservableCounter`1>
- **UDC 统一和华联**: <xref:System.Diagnostics.Metrics.UpDownCounter`1>
- **零零零零**: <xref:System.Diagnostics.Metrics.ObservableGauge`1>
- **H**: <xref:System.Diagnostics.Metrics.Histogram`1>

有些仪器保留了历史类型或单位字符串。下面的描述说明了运行时间行为,以便查询可以保存这些语义。

<a id="application-requests-and-clients"></a>

## 申请请求和客户

| 文书文书文书文书 | 类型类型 | 股股股股 | 属性 | 说明 |
|---|---|---|---|---|
| `orleans-app-requests-canceled` | C | 默示要求 | `grain_type` | 出 grain 被他们的电话人取消请求 |
| `orleans-app-requests-latency` | H | `ms` | - | 完成外出请求回馈的通话时间的间隔。 使用出口商视图配置具体应用程序的桶底界限 。 |
| `orleans-app-requests-timedout` | C | 默示要求 | `grain_type` | 超出配置响应超时的外传 grain 请求。 |
| `orleans-client-connected-gateways` | 零零零零 | 隐式网关 | - | 由 Orleans 客户端目前持有的网关连接 。 |

请求的延迟时间覆盖呼叫者回调完成之前的间隔,包括结束超时、取消、目标-silo 失败和主机休眠路径。

<a id="networking-gateways-and-messaging"></a>

## 联网、网关和短信

| 文书文书文书文书 | 类型类型 | 股股股股 | 属性 | 说明 |
|---|---|---|---|---|
| `orleans-gateway-connected-clients` | UDC 统一和华联 | 隐含客户 | - | 目前由 silo 网关跟踪的客户身份,包括在配置的暂停使用期间保留的断开客户端。 |
| `orleans-gateway-load-shedding` | C | 暗含信息 | - | 客户请求被拒绝,因为网关超载探测器报告饱和。 |
| `orleans-gateway-received` | C | 暗含信息 | - | silo 通过客户端网关连接接收的信件。 |
| `orleans-gateway-sent` | C | 暗含信息 | - | 网关信息发送事件,包括给客户端连接的文字。 |
| `orleans-messaging-expired` | C | 暗含信息 | `Phase` | 在 `Send`、 `Receive`、 `Dispatch`、 `Invoke`或 `Respond`期间过期的邮件。 |
| `orleans-messaging-pings-received` | C | 暗中的 | `Destination` | 成员查询从已查明的 silo 地址收到的信息。 |
| `orleans-messaging-pings-reply-missed` | C | 默示答复 | `Destination` | 直接或间接成员调查没有得到已查明的 silo 地址成功答复。 |
| `orleans-messaging-pings-reply-received` | C | 默示答复 | `Destination` | 成功直接或间接的成员从已确定的 silo 地址中寻找答复。 |
| `orleans-messaging-pings-sent` | C | 暗中的 | `Destination` | 成员查询发送到已确认的 silo 地址的信息。 |
| `orleans-messaging-processing-activation-data` | 零零零零 | 默示要求 | - | 目前排队或执行的请求 grain 启动 silo. |
| `orleans-messaging-processing-dispatcher-forwarded` | OC(石油公司) | 暗含信息 | - | 调度员发送的信息。 |
| `orleans-messaging-processing-dispatcher-processed` | OC(石油公司) | 暗含信息 | `Direction`, `Status` | 由发件人处理的、按信件方向和 `Ok` 或 `Error` 结果拆分的信件。 |
| `orleans-messaging-processing-dispatcher-received` | OC(石油公司) | 暗含信息 | `Context`, `Direction` | 发件人收到的信息,按信件方向和是否存在 grain 运行时段进行分割。 |
| `orleans-messaging-processing-ima-enqueued` | OC(石油公司) | 暗含信息 | `Context` | 排队到收件信件代理目标的邮件: `ToNull`、 `ToSystemTarget`或 `ToGrain`。 |
| `orleans-messaging-processing-ima-received` | OC(石油公司) | 暗含信息 | - | 收到信息后,收到信息代理。 |
| `orleans-messaging-received-header-size` | OC(石油公司) | `bytes` | - | 程序收到的累计序列页头字字节。 |
| `orleans-messaging-received-messages-size` | H | `bytes` | `ConnectionDirection`、 `MessageDirection`、任择 `silo` | 收到的信息总数量分布。 |
| `orleans-messaging-rejected` | C | 暗含信息 | `Direction` | 运行时拒绝的消息 。 |
| `orleans-messaging-rerouted` | C | 暗含信息 | `Direction` | 无法使用原始路径后更改信件的路径 。 |
| `orleans-messaging-sent-dropped` | C | 暗含信息 | `Direction` | 发送信息成功之前,即已发出。 |
| `orleans-messaging-sent-failed` | C | 暗含信息 | `Direction` | 发送失败的外传信息。 |
| `orleans-messaging-sent-header-size` | OC(石油公司) | `bytes` | - | 程序发送的累计序列页头字节。 |
| `orleans-messaging-sent-local` | OC(石油公司) | 暗含信息 | - | 本地发送的讯息没有网络发送 。 |
| `orleans-messaging-sent-messages-size` | H | `bytes` | `ConnectionDirection`、 `MessageDirection`、任择 `silo` | 发送信件总大小的分布。 |
| `orleans-networking-sockets-closed` | C | 隐含的袜索 | `Direction` | 网络连接关闭, 按连接方向分隔 。 |
| `orleans-networking-sockets-opened` | C | 隐含的袜索 | `Direction` | 打开网络连接, 由连接方向分隔 。 |

`Destination` 和 `silo` 含有 silo 地址和内装物。在事故诊断期间有用,可以在重新启动时创建新的时间序列。

<a id="scheduling-activations-and-grains"></a>

## 排程、启动和 grains

| 文书文书文书文书 | 类型类型 | 股股股股 | 属性 | 说明 |
|---|---|---|---|---|
| `orleans-catalog-activation-collections` | C | 暗扫描 | - | silo进行的动能感应扫描。 |
| `orleans-catalog-activation-concurrent-registration-attempts` | C | 暗中尝试, | `grain_type` | 已经在其他地点登记的启动登记中遇到的激活登记。 |
| `orleans-catalog-activation-created` | C | 内含的激活 | `grain_type` | Grain 由 silo 包括后来失败启动的尝试。 |
| `orleans-catalog-activation-destroyed` | C | 内含的激活 | `grain_type` | Grain 上下文从 silo 目录中成功注册。 |
| `orleans-catalog-activation-failed-to-activate` | C | 内含的激活 | `grain_type` | 其生命周期启动失败或被取消的活动。 |
| `orleans-catalog-activation-latency` | H | `ms` | `grain_type`, `status`, `directory` | 激活期,按结果和 grain 是否使用目录(`enabled` 或 `disabled`)分列。状态值是 `success`、 `canceled`、 `directory_error`、 `duplicate`和 `error`。 |
| `orleans-catalog-activation-non-existent` | C | 暗含信息 | `grain_type` | 请求新激活, 且该目录在 silo 不活动时无法创建, 并标有目标的语句类型 grain 。 |
| `orleans-catalog-activation-shutdown` | C | 隐含的关闭事件 | `via`,用于个别激活的 `grain_type` | 激活关闭除以 `collection`、 `deactivateOnIdle`、 `deactivateStuckActivation`或 `migration`。包括每个非空收藏批次的历史性额外事件,仅标有 `via=collection`。 |
| `orleans-catalog-activation-working-set` | 零零零零 | 内含的激活 | `grain_type` | 最近积极开展这项工作 silo,在工作安排驱逐或停止雇用之前计算。 |
| `orleans-catalog-activations` | 零零零零 | 隐含的目录条目 | `grain_type` | 目前在当地启动目录中登记的目标,包括系统目标和无国籍者-工人群体背景。 |
| `orleans-catalog-deactivation-latency` | H | `ms` | `grain_type`, `via` | 关闭路径分隔为关闭路径。 `via=unknown` 在选择关闭路径之前先确认完成时间 。 |
| `orleans-grains` | UDC 统一和华联 | Grains,隐含 | `grain_type` | 构建了 grain 例,在案件分配上加插和处置时减少。 |
| `orleans-scheduler-long-running-turns` | C | 旋转,隐含 | - | Grain 微转,其同步执行超过 <xref:Orleans.Configuration.SchedulingOptions.TurnWarningLengthThreshold>。 |
| `orleans-system-targets` | UDC 统一和华联 | 系统目标,隐含 | `type` | 按类型分列的当前 Orleans 系统目标实例。 |

<a id="grain-type-identity-and-aggregation"></a>

### Grain- 类型识别和聚合

`grain_type` 是目标 <xref:Orleans.Runtime.GrainType>的直线字符串表示,相当于 `GrainId.Type.ToString()`。每个激活和定向电文都在其 grain ID中含有这一特性。 明确 grain-类型名称和构建的通用参数是该特性的一部分,包括一个明确命名的 grain `unknown`。其值可能不同于CLR执行名称。

激活指标在 silo 一生中将能量弦和人口计数器隐藏于每个登记类型。所有类型的激活都分享了这个元数据。拒绝启动请求重新使用缓存名称,并在有时以其他方式格式为该事件的目标 grain 类型格式,保持缓存增长与类型注册挂钩;工作设置插入/清除和激活-指令登记/删除更新相应的人口。一个刮号列出类型计数器,包括最后成员离开后零计数。暴露无 grain 身份的工类成员贡献了一个未标记的人口。第一个闲置的工作分类扫描保留了一名成员;驱逐了成员;完成过渡使每类和未标记的金额与相应当地居民相协调;同时出现的废料可以观察正在发生的转变。

生命周期反转记录添加事件。 将不同类型和任何未加标记的投放器的增加数量和用于相同发泄器和间隔的附加收集批次停机事件以回收总事件量, 收藏扫描和额外的收缴批次停机事件保留其分批范围; 单个启动事件带有 grain 类型。 完成激活尝试有助于一次含结果和目录标签的潜伏观察; 已完成的失能会有助于一次观测, 并使用关闭路径。 直方图在操作开始时启用时开始计时 。

构建的内存计数储存在相同的每类元数据中,并在试派和处置期间以原子方式更新。管理统计读取这些对应数据,并将收集快照时共享一个CLR实施名称的类型组合在一起;量度事件保留了每一种 grain类型的身份。

一组总数,选择每一排放的最近新观测结果 silo 和类型,然后在 silos。保存不同的排放源身份,结束过时的观测,并实现后端当前值的测量仪。重复的擦拭有助于连续的拍摄。 `orleans-grains` 转而供应注册的试样三角洲,出口商从收集开始就汇总。

无国籍者-工人群体提供一份目录条目,并可以包含若干已建工。 活动失败可以在清理过程中暂时留下一个目录条目。 在移民过程中,来源和目的地负责自己的登记和清除。

`orleans-grains` 键/价值迁移和仪表板更新见 [动能指标系统的最新计划](../../migration-guide.md#activation-metric-schema-update)。

<a id="cluster-manifests"></a>

## 群集显示器

| 文书文书文书文书 | 类型类型 | 股股股股 | 属性 | 说明 |
|---|---|---|---|---|
| `orleans-manifest-cache-lookups` | C | 隐性调查 | `result`, `source` | 内容缓缓查查找 `hit` 或 `miss` 来源于 `silo` 散数响应或 a `peer` 摘要。 |
| `orleans-manifest-fallbacks` | C | 隐存检索 | `reason` | 散列回收回落到直接的 RPC : `error`、 `missing`或 `mismatch`。 |
| `orleans-manifest-peer-probes` | C | 暗中尝试, | `status` | 本地同行尝试完成后, `success`、 `timeout`、 `canceled`或 `error`; `skipped` 点算当地拒绝的尝试。 |
| `orleans-manifest-peer-repairs` | C | Silo 条目,隐含 | - | 成功公布的同侪修理提供的缺失 silo 清单条目。 |
| `orleans-manifest-retrieval-duration` | H | `ms` | `mode`, `status` | 当地当地 silo- 最显著的检索时间 `direct` 或 `hash` 模式模式,以 `success`, `error`,或 `canceled`. |

分类是固定的,没有 silo 地址、 显示的 hashes 或 grain 类型名称。 测试取消包括由完全直接结果取代的可选工作。 发布、 回溯和推出语义请见 [集群数据单检索](../../implementation/cluster-manifest-retrieval.md) 。

<a id="grain-directory-and-consistent-rings"></a>

## Grain 目录和一致环

| 文书文书文书文书 | 类型类型 | 股股股股 | 属性 | 说明 |
|---|---|---|---|---|
| `orleans-consistent-ring-range-percentage-average` | 零零零零 | 隐含百分比 | - | 一种 silo (`100 / ring size`)在一致环中所占份额比例相等。 |
| `orleans-consistent-ring-range-percentage-local` | 零零零零 | 隐含百分比 | - | 占目前属于该 silo的一致环的百分比。 |
| `orleans-consistent-ring-size` | 零零零零 | Silos,隐含 | - | 会员们在一贯的黑环圈里 |
| `orleans-directory-cache-size` | 零零零零 | 隐含条目 | - | 注册的本地 grain- 指令缓存条目总条目数 。 |
| `orleans-directory-lookups-cache-issued` | C | 隐性调查 | - | Grain- 定位搜索试图针对此 silo目录缓存进行。 |
| `orleans-directory-lookups-cache-successes` | C | 隐性调查 | - | 返回可用条目的本地目录缓存查询 。 |
| `orleans-directory-lookups-full-issued` | C | 隐性调查 | - | 完整 grain- 由此引发的指令性调查 silo. |
| `orleans-directory-lookups-local-directory-issued` | C | 隐性调查 | - | 直接针对遗留目录分区发布搜索 。 |
| `orleans-directory-lookups-local-directory-successes` | C | 隐性调查 | - | 直接的遗留区调查,返回了 grain 地址。 |
| `orleans-directory-lookups-local-issued` | C | 隐性调查 | - | Grain-通过遗留 grain 定位器发布位置查询。 |
| `orleans-directory-lookups-local-successes` | C | 隐性调查 | - | 遗留 grain-locator lookups 返回 grain 地址的定位器查询。 |
| `orleans-directory-lookups-remote-received` | C | 隐性调查 | - | 从另一个 silo收到的转发目录查询请求 。 |
| `orleans-directory-lookups-remote-sent` | C | 隐性调查 | - | 向另一个 silo转发目录查询请求 。 |
| `orleans-directory-partition-size` | 零零零零 | 隐含条目 | - | Grain 本登记登记 silo遗留目录分割区 。 |
| `orleans-directory-range-lock-held-duration` | H | 毫米, 隐含 | - | 时间目录范围锁定分布情况已保持。 |
| `orleans-directory-recovery-count` | C | 回收,隐含 | - | 成员变动后完成的回收范围目录。 |
| `orleans-directory-recovery-duration` | H | 毫米, 隐含 | - | 完成的目录范围回收期限分布。 |
| `orleans-directory-registration-duration` | H | `ms` | `locator`, `status` | Grain 执行定位和 `success`, `canceled`,或 `error` 结果。 |
| `orleans-directory-registrations` | C | 登记,默示 | `locator`, `status` | 已完成 grain 登记尝试,通过定位器执行和结果。 |
| `orleans-directory-registrations-single-act-issued` | C | 登记,默示 | - | 就此 silo启动的单一激活登记申请。 |
| `orleans-directory-registrations-single-act-local` | C | 登记,默示 | - | 由此 silo遗留目录分区处理的单激活登记。 |
| `orleans-directory-registrations-single-act-remote-received` | C | 登记,默示 | - | 从另一个 silo收到的转交的单启动登记申请。 |
| `orleans-directory-registrations-single-act-remote-sent` | C | 登记,默示 | - | 向另一个 silo发送的单一激活登记申请。 |
| `orleans-directory-ring-local-portion-average-percentage` | 零零零零 | 隐含百分比 | - | 继承 grain-- -- 指令环(`100 / ring size`)的等额份额百分比。 |
| `orleans-directory-ring-local-portion-distance` | 零零零零 | 含散闭距离 | - | 与后继者 silo 之间距离的原散星环。 |
| `orleans-directory-ring-local-portion-percentage` | 零零零零 | 隐含百分比 | - | 遗留 grain-分配给本 silo的指令圈的百分比。 |
| `orleans-directory-ring-size` | 零零零零 | Silos,隐含 | - | 遗留 grain-- -- 指令圈的成员。 |
| `orleans-directory-snapshot-transfer-count` | C | 内隐转让 | - | 从上一个分区所有者成功接收到目录范围快照 。 |
| `orleans-directory-snapshot-transfer-duration` | H | 毫米, 隐含 | - | 成功分发快递目录的最后期限。 |
| `orleans-directory-unregistrations-issued` | C | 默示不登记, | - | 单一 grain- 就本 silo启动的未登记申请。 |
| `orleans-directory-unregistrations-local` | C | 默示不登记, | - | 单一 grain- 由该 silo的遗留目录分割处理的未登记请求。 |
| `orleans-directory-unregistrations-many-issued` | C | 默示要求 | - | 批发 grain- 根据本 silo启动的未登记申请。 |
| `orleans-directory-unregistrations-many-remote-received` | C | 默示要求 | - | 收到另一批 silo的未登记申请。 |
| `orleans-directory-unregistrations-many-remote-sent` | C | 默示要求 | - | 向另一 silo转交的批发未登记申请。 |
| `orleans-directory-unregistrations-remote-received` | C | 默示不登记, | - | 转交从另一个 silo收到的单一未登记申请。 |
| `orleans-directory-unregistrations-remote-sent` | C | 默示不登记, | - | 向另一 silo转交的单一未登记申请。 |
| `orleans-directory-validations-cache-received` | C | 默示要求 | - | 从另一个 silo收到的批次目录缓存核证请求。 |

<a id="reminders-storage-runtime-resources-and-watchdog"></a>

## 提醒、储存、运行时间资源和监督

| 文书文书文书文书 | 类型类型 | 股股股股 | 属性 | 说明 |
|---|---|---|---|---|
| `orleans-reminders-active` | 零零零零 | 默示提醒 | - | 在当地追踪的提醒信上方被绑着,包括临时墓碑。 |
| `orleans-reminders-tardiness` | H | `seconds` | - | 提醒时间与发送时间之间的延迟。 |
| `orleans-reminders-ticks-delivered` | C | 下滴答, 隐含 | - | 提醒计数成功传送到 `IRemindable.ReceiveReminder`。 |
| `orleans-runtime-available-memory` | 零零零零 | `MB` | - | GC报告的现有记忆预算,除以1,024平方。 |
| `orleans-runtime-total-physical-memory` | 零零零零 | `MB` | - | <xref:System.GCMemoryInfo.TotalAvailableMemoryBytes>,除以1,024平方。历史名称可以代表一个过程或容器内存限,而不是主机的物理内存限制。 |
| `orleans-storage-clear-errors` | C | 隐含错误 | `provider_type_name`, `state_name`, `state_type` | Grain- 说明排除例外的清晰操作。 |
| `orleans-storage-clear-latency` | H | `ms` | `provider_type_name`, `state_name`, `state_type` | 顺利完成 grain- 状态清晰作业的延迟期。 |
| `orleans-storage-read-errors` | C | 隐含错误 | `provider_type_name`, `state_name`, `state_type` | Grain- 状态读取放弃例外的操作。 |
| `orleans-storage-read-latency` | H | `ms` | `provider_type_name`, `state_name`, `state_type` | 成功完成 grain- state repecial. |
| `orleans-storage-write-errors` | C | 隐含错误 | `provider_type_name`, `state_name`, `state_type` | Grain- 国家书面作业,但有例外。 |
| `orleans-storage-write-latency` | H | `ms` | `provider_type_name`, `state_name`, `state_type` | 成功完成 grain- state write. 的延迟时间。 |
| `orleans-watchdog-health-checks` | C | 隐含支票 | - | 进行体检的运行时间监督。 |
| `orleans-watchdog-health-checks-failed` | C | 隐含支票 | - | 运行时间监督健康检查报告失败。 |

<a id="streaming"></a>

## 串流

钚/子仪器使用 `provider` 和 `grain_type`。持久性流读仪器使用 `provider` 和 `queue`;发送仪器使用 `provider` 和 `grain_type`。默认接收器和缓存监测器使用 `QueueId`,默认区块库监测器使用 `BlockPoolId`。自定义监测器的安装可以选择不同层面。

| 文书文书文书文书 | 类型类型 | 股股股股 | 属性 | 说明 |
|---|---|---|---|---|
| `orleans-streams-block-pool-allocated-memory` | OC(石油公司) | `bytes` | 默认: `BlockPoolId` | 从流流缓冲池中分配的累计字节。 |
| `orleans-streams-block-pool-available-memory` | OC(石油公司) | `bytes` | 默认: `BlockPoolId` | 流动缓冲池报告的现有可用字节。 这个点点值作为可观测计数器执行 。 |
| `orleans-streams-block-pool-claimed-memory` | OC(石油公司) | `bytes` | 默认: `BlockPoolId` | 流缓冲池报告的现有索赔字节。 这个点点值作为可观测计数器执行。 |
| `orleans-streams-block-pool-released-memory` | OC(石油公司) | `bytes` | 默认: `BlockPoolId` | 向溪流缓冲池释放的累计字节。 |
| `orleans-streams-block-pool-total-memory` | OC(石油公司) | `bytes` | 默认: `BlockPoolId` | 流流缓冲池报告的现有总字节。 这个点点值作为可观测计数器执行。 |
| `orleans-streams-persistent-stream-messages-read` | C | 隐含的批货集装箱 | `provider`, `queue` | 批发集装箱从持久性流接收器队列中读取,一个集装箱可举行多个流事件。 |
| `orleans-streams-persistent-stream-messages-sent` | C | 隐含的批货集装箱 | `provider`, `grain_type` | 批发集装箱选择供消费者交付。在交货前的反增量,所以这不是一个成功的交付计算。 |
| `orleans-streams-persistent-stream-pubsub-cache-size` | 零零零零 | 隐含条目 | `name` | 由持久性流动拉动剂持有的钚/子缓存条目。 |
| `orleans-streams-persistent-stream-pulling-agents` | 零零零零 | 隐含 | `name` | 目前由这一 silo的持久性流供应商拥有的拉动剂。 |
| `orleans-streams-pubsub-consumers` | C, 签署三角三角洲 | 消费者,隐含 | `provider`, `grain_type` | 使用正增加值和负清除值,在此过程中记录的已签名的累计消费登记三角洲。 该系列不是从持续存在的公酒吧/子州初始化的,也不是时间点数。 |
| `orleans-streams-pubsub-consumers-added` | C | 暗中尝试, | `provider`, `grain_type` | 试图进行消费者登记。 |
| `orleans-streams-pubsub-consumers-removed` | C | 暗中尝试, | `provider`, `grain_type` | 普布/次消费者未登记尝试。 |
| `orleans-streams-pubsub-producers` | C, 签署三角三角洲 | 暗生产者, | `provider`, `grain_type` | 使用正增加值和负清除值,在此过程中记录的已签名的累计生产者登记三角洲。该系列不是从持续存在的公关/子州开始启动的,也不是时间点数。 |
| `orleans-streams-pubsub-producers-added` | C | 暗中尝试, | `provider`, `grain_type` | 生产/分生产者登记尝试。 |
| `orleans-streams-pubsub-producers-removed` | C | 暗中尝试, | `provider`, `grain_type` | 未经注册的试样。 |
| `orleans-streams-queue-cache-length` | OC(石油公司) | `messages` | 默认: `QueueId` | 队列缓存中当前信件的数量。 此点在时间值将作为可观察的计数器执行 。 |
| `orleans-streams-queue-cache-memory-allocated` | OC(石油公司) | 隐含字节 | 默认: `QueueId` | 由队列缓存分配的累积字节 。 |
| `orleans-streams-queue-cache-memory-released` | OC(石油公司) | 隐含字节 | 默认: `QueueId` | 由队列缓存释放的累积字节 。 |
| `orleans-streams-queue-cache-messages-added` | OC(石油公司) | 暗含信息 | 默认: `QueueId` | 累积信件添加到队列缓存中 。 |
| `orleans-streams-queue-cache-messages-purged` | OC(石油公司) | 暗含信息 | 默认: `QueueId` | 从队列缓存中清除累积信件 。 |
| `orleans-streams-queue-cache-oldest-age` | 零零零零 | `TimeSpan` 勾滴, 隐含 | 默认: `QueueId` | 最古老的被解密信息 仍然由缓存监视器代表 |
| `orleans-streams-queue-cache-oldest-to-newest-duration` | 零零零零 | `TimeSpan` 勾滴, 隐含 | 默认: `QueueId` | 后端时间范围是从缓存监视器所代表的最古老到最新的信件。 |
| `orleans-streams-queue-cache-pressure` | 零零零零 | 内含比率 | 默认: `QueueId`, `PressureMonitorType` | 每个缓存压力监测器都报告当前的压力值。 |
| `orleans-streams-queue-cache-pressure-contribution-count` | 零零零零 | 内含捐款 | 默认: `QueueId`, `PressureMonitorType` | 每个缓冲压力监测器都报告了压力-贡献计数。 |
| `orleans-streams-queue-cache-size` | OC(石油公司) | `bytes` | 默认: `QueueId` | 当前队列缓存大小。 此点在时间值作为可观察的计数器执行 。 |
| `orleans-streams-queue-cache-under-pressure` | 零零零零 | 隐含的布尔(`0` 或 `1`) | 默认: `QueueId`, `PressureMonitorType` | 是否每个缓存压力监测器 都报告目前的压力。 |
| `orleans-streams-queue-initialization-duration` | C | `TimeSpan` 勾滴, 隐含 | 默认: `QueueId` | 队列接收器初始化电话累计时间。 |
| `orleans-streams-queue-initialization-exceptions` | C | 隐含例外, | 默认: `QueueId` | 将提供例外的接收器初始化呼叫排队。 |
| `orleans-streams-queue-initialization-failures` | C | 失败,隐含 | 默认: `QueueId` | 将接收器初始化电话排成队,据报未成功。 |
| `orleans-streams-queue-messages-received` | OC(石油公司) | 暗含信息 | 默认: `QueueId` | 由队列组别收到的累积信息。 |
| `orleans-streams-queue-newest-message-enqueue-age` | 零零零零 | `TimeSpan` 勾滴, 隐含 | 默认: `QueueId` | 最新队列读取时返回的最新消息的时代 。 |
| `orleans-streams-queue-oldest-message-enqueue-age` | 零零零零 | `TimeSpan` 勾滴, 隐含 | 默认: `QueueId` | 最老的讯息年龄由最新的队列读回。 |
| `orleans-streams-queue-read-duration` | C | `TimeSpan` 勾滴, 隐含 | 默认: `QueueId` | 队列接收器累计读电话时间。 |
| `orleans-streams-queue-read-exceptions` | C | 隐含例外, | 默认: `QueueId` | 将接收器排队,读取提供例外的电话。 |
| `orleans-streams-queue-read-failures` | C | 失败,隐含 | 默认: `QueueId` | 将接收器排队, 读取被报告为未成功电话 。 |
| `orleans-streams-queue-shutdown-duration` | C | `TimeSpan` 勾滴, 隐含 | 默认: `QueueId` | 排队接收器停机电话累计持续时间。 |
| `orleans-streams-queue-shutdown-exceptions` | C | 隐含例外, | 默认: `QueueId` | 将提供例外的接收器关闭电话排成队列。 |
| `orleans-streams-queue-shutdown-failures` | C | 失败,隐含 | 默认: `QueueId` | 将接收器关闭电话排成队,据报未成功。 |

失败和例外计数器可以同时对一个操作进行递增。 期限用来对总过错计数; 在计算一个窗口平均值时,将其增加数除以匹配计算算法。

<a id="transactions"></a>

## 交易交易

| 文书文书文书文书 | 类型类型 | 股股股股 | 属性 | 说明 |
|---|---|---|---|---|
| `orleans-transactions-failed` | OC(石油公司) | 隐性交易,即 | - | 完成的交易没有成功。 |
| `orleans-transactions-started` | OC(石油公司) | 隐性交易,即 | - | 由当地交易代理开始的交易。 |
| `orleans-transactions-successful` | OC(石油公司) | 隐性交易,即 | - | 交易成功完成 。 |
| `orleans-transactions-throttled` | OC(石油公司) | 隐性交易,即 | - | 交易代理暂停交易,拒绝交易尝试。 |

<a id="durable-jobs"></a>

## 持久就业

| 文书文书文书文书 | 类型类型 | 股股股股 | 属性 | 说明 |
|---|---|---|---|---|
| `orleans-durablejobs-cancel-job-call-duration` | H | `ms` | `status` | 有效期 `CancelAsync` 平分 `cancellation_requested`, `not_found`, `operation_canceled`,或 `error`. |
| `orleans-durablejobs-cancel-job-calls` | C | 暗内呼吁 | `status` | 已完成 `CancelAsync` 请求,按要求结果进行。 |
| `orleans-durablejobs-handler-execution-duration` | H | `ms` | `status` | 完成处理者处决的期限,除以 `completed`、 `attempt_canceled`或 `failed`。 |
| `orleans-durablejobs-handler-executions` | C | 处决,含 | `status` | 手法处决最终结果。 |
| `orleans-durablejobs-handler-executions-started` | C | 处决,含 | - | 执行处决开始。 |
| `orleans-durablejobs-job-attempt-duration` | H | `ms` | `status` | 工作尝试期以 `completed`、 `failed`、 `retried`或 `rescheduled`结束。 |
| `orleans-durablejobs-job-attempts-started` | C | 暗中尝试, | - | 开始尝试长期工作。 |
| `orleans-durablejobs-job-dispatch-lag` | H | `ms` | - | 工作到开始的时候 都拖延了 |
| `orleans-durablejobs-job-schedule-duration` | H | `ms` | - | 需要时间才能坚持并安排工作。 |
| `orleans-durablejobs-job-cancellation-requests` | C | 默示要求 | - | 长期有效的取消工作申请,这些申请已记录下来。 |
| `orleans-durablejobs-jobs-completed` | C | 隐性工作,含 | - | 成功完成了持久工作。 |
| `orleans-durablejobs-jobs-failed` | C | 隐性工作,含 | - | 持久工作尝试记录为失败。 |
| `orleans-durablejobs-jobs-retried` | C | 隐性工作,含 | - | 计划重新尝试的持久工作尝试。 |
| `orleans-durablejobs-jobs-scheduled` | C | 隐性工作,含 | - | 成功安排了持久工作。 |
| `orleans-durablejobs-ownership-check-duration` | H | `ms` | - | 硬性所有权检查的持续时间。 |
| `orleans-durablejobs-schedule-job-call-duration` | H | `ms` | `status` | 工作时间表 API 调用时间,除以 `ok`、 `operation_canceled`或 `error`。 |
| `orleans-durablejobs-schedule-job-calls` | C | 暗内呼吁 | `status` | 已完成的日程安排工作 API 呼叫按结果分列。 |
| `orleans-durablejobs-shard-batch-bytes` | H | `bytes` | - | 耐用工作碎片突变批次的序号小字节大小。 |
| `orleans-durablejobs-shard-batch-mutations` | H | 隐含变异, | - | 耐用工作碎片批次的变异计数 |
| `orleans-durablejobs-shard-pending-depth` | H | 隐含变异, | - | 处理前,在每批散装货中收集到的突变作业之前。 |
| `orleans-durablejobs-shard-processing-duration` | H | `ms` | `status` | 碎片处理时间,除以 `completed`、 `attempt_canceled`或 `error`。 |
| `orleans-durablejobs-shards-processed` | C | 纸质碎片 | `status` | 碎件处理通行证按结果填写。 |
| `orleans-durablejobs-storage-batch-size` | H | 隐含变异, | `status` | 应用硬变异 包括在每个州-书面尝试中, 以结果分开。 |
| `orleans-durablejobs-storage-batches` | C | 含蓄电池筒 | `status` | 长期储存的批次除以 `ok`、 `operation_canceled`或 `error`。 |
| `orleans-durablejobs-stripe-distribution` | C | 默示转让, | `stripe` | 按持久工作条纹编号分配的工作。 |

<a id="journaling"></a>

## 日刊

核心日记工具酌情使用 `operation` 和 `status` 。操作值包括 `append`、 `snapshot`、 `replace`、 `read`、 `delete`和 `recovery`;状态是 `ok` 或 `error`。

| 文书文书文书文书 | 类型类型 | 股股股股 | 属性 | 说明 |
|---|---|---|---|---|
| `orleans-journaling-compaction-triggers` | C | 暗中触发器 | `reason` | 要求印发 `user_snapshot`, `storage_requested`,或 `migration`. |
| `orleans-journaling-gather-duration` | H | `ms` | `operation` | 收集日记的时间用于存储操作。 |
| `orleans-journaling-recoveries` | C | 回收,隐含 | `operation`, `status` | 按结果分列的日志回收尝试。 |
| `orleans-journaling-recovery-duration` | H | `ms` | `operation`, `status` | 按成果分列的日志回收期。 |
| `orleans-journaling-state-delete-duration` | H | `ms` | `operation`, `status` | 日记状态删除请求的期限。 |
| `orleans-journaling-state-delete-requests` | C | 默示要求 | `operation`, `status` | 日刊-状态删除按结果分列的请求。 |
| `orleans-journaling-state-scan-count` | H | 国家,默示 | `operation` | 日志显示在为某项作业收集工作时进行扫描。 |
| `orleans-journaling-state-write-duration` | H | `ms` | `operation`, `status` | 日记国家书面请求的期限。 |
| `orleans-journaling-state-write-requests` | C | 默示要求 | `operation`, `status` | 按业务和结果分列的已公布的书面请求。 |
| `orleans-journaling-storage-bytes` | C | `bytes` | `operation` | 日记存储业务成功处理的累积字节。 |
| `orleans-journaling-storage-operation-bytes` | H | `bytes` | `operation`, `status` | 成功分发日记存储操作规模。 |
| `orleans-journaling-storage-operation-duration` | H | `ms` | `operation`, `status` | 按业务和结果分列的日记储存运行期限。 |
| `orleans-journaling-storage-operation-queue-duration` | H | `ms` | `operation`, `status` | 时间日记存储操作在执行前排队。 |
| `orleans-journaling-storage-operations` | C | 隐含业务, | `operation`, `status` | 按业务和结果分列的日记储存业务。 |
| `orleans-journaling-write-coalesced-callers` | H | 暗中呼叫者 | `operation` | 在每个联合日记笔记本中,将呼叫者数合并在一起。 |

<a id="provider-workload-counters"></a>

### 供应商工作量计数

供应商对账记录
。每个提供者库为 `provider` 标记定义自己的简称: `s3`,
`azure_blob`, `azure_table`, `redis`,或 `volatile`现有的国家管理人员和具体供应商
供应工具的储存运行时长、结果和字节。

| 文书文书文书文书 | 类型类型 | 股股股股 | 属性 | 说明 |
|---|---|---|---|---|
| `orleans-journaling-provider-catalog-entries` | C | 隐含条目 | `provider` | 向消费者发送的目录条目。 |
| `orleans-journaling-provider-catalog-pages` | C | 暗页,隐性 | `provider` | S3和 Azure 目录跨页期间收到的页面,包括空页。 |
| `orleans-journaling-provider-catalog-items` | C | 隐含项目, | `provider` | 过滤前或目录跨行所消耗的 Redis 扫描键之前, 本地目录页面候选人 。 |
| `orleans-journaling-provider-retries` | C | 默示回签,内含 | `provider`, `reason` | 使用有条不紊的理由标签的 明目张胆的供应商-用户循环雷管。 |

S3和 Azure 计算经编的考试目录收到的每一页以及该页上的所有候选人,
本地过滤前的本地过滤。 Redis 计取过滤和重复之前跨行所消耗的密钥
控制多变存储记录交付的条目。
条目显示提供者过滤器的数据数量。

空的逻辑逻辑范围产生零页、项目和条目。
保留已记录的数字。 终端点查预览或错误的页面检索添加
提供方重试数描述明确的存储回收循环。

使用现有时间段直方图进行储存内嵌嵌入和
[东道国提供托管提供扶养用人](../../grains/journaling/operations.md#use-host-provided-dependency-telemetry)
SDK 请求的时间、结果和运输尝试。
服务方交易计量为账单调节提供了基础。

下文现有的国家管理和具体供应商文书保留其原始含义。

Azure 和S3储存-方法工具使用操作 `create`、 `get_metadata`、 `update_metadata`、 `append`、 `delete`、 `read`和 `replace`。

| 文书文书文书文书 | 类型类型 | 股股股股 | 属性 | 说明 |
|---|---|---|---|---|
| `orleans-journaling-azure-blob-operation-bytes` | C | `bytes` | `operation` | 由 Azure Blob日记账存储操作成功处理的累计字节。 |
| `orleans-journaling-azure-blob-operation-duration` | H | `ms` | `operation`, `status` | Azure 球状日记账存储运行期,按运行和结果分列。 |
| `orleans-journaling-azure-blob-operations` | C | 隐含业务, | `operation`, `status` | Azure 黑球日记账存储业务,按运行和 `ok` 或 `error` 状态分列。 |
| `orleans-journaling-azure-table-operation-bytes` | C | `bytes` | `operation` | Azure 表日记账存储操作成功处理的累计字节。 |
| `orleans-journaling-azure-table-operation-duration` | H | `ms` | `operation`, `status` | Azure 表格日记账存储运行期,按运行和结果分列。 |
| `orleans-journaling-azure-table-operations` | C | 隐含业务, | `operation`, `status` | Azure 表日记账存储业务,按运行和 `ok` 或 `error` 状态分列。 |
| `orleans-journaling-s3-operation-bytes` | C | `bytes` | `operation` | 以S3日记存储方法成功处理的累积字节。 |
| `orleans-journaling-s3-operation-duration` | H | `ms` | `operation`, `status` | S3 日记存储方法的有效期,按运行和结果分列。 |
| `orleans-journaling-s3-operations` | C | 隐含业务, | `operation`, `status` | S3 日记存储方法,按运行和 `ok` 或 `error` 状态分列。 |

<a id="use-the-catalog"></a>

## 使用目录

使用 [监测 Orleans 指标](metrics.md) 选择健康指标,根据类型将每个工具汇总,控制主要特征并建立警报。使用 [排除故障 Orleans 事件](troubleshooting.md) 与日志、跟踪、部署变化和依赖性健康相关。
