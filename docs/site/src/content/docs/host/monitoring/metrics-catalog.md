---
title: Orleans 指标目录
description: Microsoft.Orleans Meter 发出指标的完整参考。
ms.date: 08/18/2026
ms.topic: reference
---

<a id="orleans-metrics-catalog"></a>

# Orleans 指标目录

此目录列出当前源代码树中为 `Microsoft.Orleans` Meter 创建的所有度量工具。核心运行时度量工具在对应组件运行时可用。流、事务、持久化作业、日志化及提供程序特定的度量工具仅在配置相应功能后出现。请检查实际部署的 Orleans 版本所发出的度量工具，因为版本及已配置的包可能不同。

**单位**列给出 Orleans 记录的自然单位。“隐式”表示度量工具未设置单位元数据。`TimeSpan` 时钟周期为 100 纳秒。**属性**列列出 Orleans 添加的属性；`service.instance.id` 等资源属性也用于标识各时间序列。

度量工具类型使用以下缩写：

- **C**: <xref:System.Diagnostics.Metrics.Counter`1>
- **OC**: <xref:System.Diagnostics.Metrics.ObservableCounter`1>
- **UDC**: <xref:System.Diagnostics.Metrics.UpDownCounter`1>
- **OG**: <xref:System.Diagnostics.Metrics.ObservableGauge`1>
- **H**: <xref:System.Diagnostics.Metrics.Histogram`1>

一些度量工具保留历史类型或单位字符串。下方描述说明实际运行时行为，使查询能够保留这些语义。

<a id="application-requests-and-clients"></a>

## 应用程序请求与客户端

| 度量工具 | 类型 | 单位 | 属性 | 描述 |
|---|---|---|---|---|
| `orleans-app-requests-canceled` | C | 请求，隐式 | `grain_type` | 由发起调用的一方取消的出站 Grain 请求。 |
| `orleans-app-requests-latency` | H | `ms` | - | 调用方观察到的已完成出站请求回调所耗时间的分布。使用导出器 View 配置应用程序特定的分桶边界。 |
| `orleans-app-requests-timedout` | C | 请求，隐式 | `grain_type` | 超过配置的响应超时时间的出站 Grain 请求。 |
| `orleans-client-connected-gateways` | OG | 网关，隐式 | - | Orleans 客户端当前持有的网关连接数。 |

请求延迟涵盖直到调用方回调完成的时间，包括终止性的超时、取消、目标 Silo 故障及主机关闭路径。

<a id="networking-gateways-and-messaging"></a>

## 网络、网关及消息传递

| 度量工具 | 类型 | 单位 | 属性 | 描述 |
|---|---|---|---|---|
| `orleans-gateway-connected-clients` | UDC | 客户端，隐式 | - | Silo 网关当前跟踪的客户端标识数，包括在配置的断开保留超时期间保留的已断开客户端。 |
| `orleans-gateway-load-shedding` | C | 消息，隐式 | - | 因网关过载检测器报告饱和而被拒绝的客户端请求。 |
| `orleans-gateway-received` | C | 消息，隐式 | - | Silo 通过客户端网关连接收到的消息。 |
| `orleans-gateway-sent` | C | 消息，隐式 | - | 网关消息发送事件，包括写入客户端连接的消息。 |
| `orleans-messaging-expired` | C | 消息，隐式 | `Phase` | 在 `Send`、`Receive`、`Dispatch`、`Invoke` 或 `Respond` 阶段过期的消息。 |
| `orleans-messaging-pings-received` | C | 探测，隐式 | `Destination` | 从指定 Silo 地址收到的成员资格探测消息。 |
| `orleans-messaging-pings-reply-missed` | C | 回复，隐式 | `Destination` | 未收到指定 Silo 地址成功回复的直接或间接成员资格探测。 |
| `orleans-messaging-pings-reply-received` | C | 回复，隐式 | `Destination` | 来自指定 Silo 地址的成功直接或间接成员资格探测回复。 |
| `orleans-messaging-pings-sent` | C | 探测，隐式 | `Destination` | 发送到指定 Silo 地址的成员资格探测消息。 |
| `orleans-messaging-processing-activation-data` | OG | 请求，隐式 | - | Silo 上所有 Grain 激活实例中当前排队或正在执行的请求。 |
| `orleans-messaging-processing-dispatcher-forwarded` | OC | 消息，隐式 | - | 由分派器转发的消息。 |
| `orleans-messaging-processing-dispatcher-processed` | OC | 消息，隐式 | `Direction`, `Status` | 由分派器处理的消息，按消息方向及 `Ok` 或 `Error` 结果区分。 |
| `orleans-messaging-processing-dispatcher-received` | OC | 消息，隐式 | `Context`, `Direction` | 分派器收到的消息，按消息方向以及是否存在 Grain 运行时上下文区分。 |
| `orleans-messaging-processing-ima-enqueued` | OC | 消息，隐式 | `Context` | 加入传入消息代理目标队列的消息：`ToNull`、`ToSystemTarget` 或 `ToGrain`。 |
| `orleans-messaging-processing-ima-received` | OC | 消息，隐式 | - | 传入消息代理收到的消息。 |
| `orleans-messaging-received-header-size` | OC | `bytes` | - | 进程收到的序列化消息头累计字节数。 |
| `orleans-messaging-received-messages-size` | H | `bytes` | `ConnectionDirection`, `MessageDirection`, 可选 `silo` | 收到的完整消息大小的分布。 |
| `orleans-messaging-rejected` | C | 消息，隐式 | `Direction` | 被运行时拒绝的消息。 |
| `orleans-messaging-rerouted` | C | 消息，隐式 | `Direction` | 原路由不可用后重新路由的消息。 |
| `orleans-messaging-sent-dropped` | C | 消息，隐式 | `Direction` | 成功传输之前被丢弃的出站消息。 |
| `orleans-messaging-sent-failed` | C | 消息，隐式 | `Direction` | 发送尝试失败的出站消息。 |
| `orleans-messaging-sent-header-size` | OC | `bytes` | - | 进程发送的序列化消息头累计字节数。 |
| `orleans-messaging-sent-local` | OC | 消息，隐式 | - | 无需网络发送即可在本地投递的消息。 |
| `orleans-messaging-sent-messages-size` | H | `bytes` | `ConnectionDirection`, `MessageDirection`, 可选 `silo` | 已发送的完整消息大小的分布。 |
| `orleans-networking-sockets-closed` | C | 套接字，隐式 | `Direction` | 已关闭的网络连接，按连接方向区分。 |
| `orleans-networking-sockets-opened` | C | 套接字，隐式 | `Direction` | 已打开的网络连接，按连接方向区分。 |

`Destination` 和 `silo` 包含 Silo 地址及实例世代。这些属性在故障诊断期间有用，也可能在重启期间创建新的时间序列。

<a id="scheduling-activations-and-grains"></a>

## 调度、激活实例及 Grain

| 度量工具 | 类型 | 单位 | 属性 | 描述 |
|---|---|---|---|---|
| `orleans-catalog-activation-collections` | C | 扫描，隐式 | - | Silo 执行的激活实例回收扫描次数。 |
| `orleans-catalog-activation-concurrent-registration-attempts` | C | 尝试，隐式 | `grain_type` | 遇到其他位置已注册激活实例的激活注册操作。 |
| `orleans-catalog-activation-created` | C | 激活实例，隐式 | `grain_type` | Silo 激活实例目录创建的 Grain 上下文，包括之后激活失败的尝试。 |
| `orleans-catalog-activation-destroyed` | C | 激活实例，隐式 | `grain_type` | 从 Silo 激活实例目录成功注销的 Grain 上下文。 |
| `orleans-catalog-activation-failed-to-activate` | C | 激活实例，隐式 | `grain_type` | 生命周期初始化失败或被取消的激活实例。 |
| `orleans-catalog-activation-latency` | H | `ms` | `grain_type`, `status`, `directory` | 激活持续时间，按结果及 Grain 是否使用目录（`enabled` 或 `disabled`）区分。状态值为 `success`、`canceled`、`directory_error`、`duplicate` 和 `error`。 |
| `orleans-catalog-activation-non-existent` | C | 消息，隐式 | `grain_type` | Silo 处于非活动状态时，激活实例目录无法创建新激活实例的请求，带有目标的规范 Grain 类型标签。 |
| `orleans-catalog-activation-shutdown` | C | 关闭事件，隐式 | `via`, `grain_type`（针对单个激活实例） | 按 `collection`、`deactivateOnIdle`、`deactivateStuckActivation` 或 `migration` 区分的激活实例关闭事件。包括每个非空回收批次的历史附加事件，该事件仅带有 `via=collection` 标签。 |
| `orleans-catalog-activation-working-set` | OG | 激活实例，隐式 | `grain_type` | 此 Silo 上最近活动的激活实例，计数保留至工作集逐出或停用。 |
| `orleans-catalog-activations` | OG | 目录条目，隐式 | `grain_type` | 当前注册在本地激活实例目录中的目标，包括系统目标及无状态工作者组上下文。 |
| `orleans-catalog-deactivation-latency` | H | `ms` | `grain_type`, `via` | 停用持续时间，按关闭路径区分。`via=unknown` 表示在选定关闭路径之前已完成停用。 |
| `orleans-grains` | UDC | Grain，隐式 | `grain_type` | 已构造的 Grain 实例，在分配实例时增加、释放实例时减少。每个无状态工作者实例分别计数。 |
| `orleans-scheduler-long-running-turns` | C | 执行轮次，隐式 | - | 同步执行时间超过 <xref:Orleans.Configuration.SchedulingOptions.TurnWarningLengthThreshold> 的 Grain 微型执行轮次。 |
| `orleans-system-targets` | UDC | 系统目标，隐式 | `type` | 按类型区分的当前 Orleans 系统目标实例数。 |

<a id="grain-type-identity-and-aggregation"></a>

### Grain 类型标识与聚合

`grain_type` 是目标 <xref:Orleans.Runtime.GrainType> 的规范字符串表示形式，等效于 `GrainId.Type.ToString()`。每个激活实例和定向消息均在其 Grain ID 中携带此标识。显式 Grain 类型名称及已构造泛型的实参是该标识的一部分，包括显式命名为 `unknown` 的 Grain。该值可能不同于 CLR 实现名称。

激活指标在 Silo 生命周期内按已注册类型缓存规范字符串及实例数量计数器。该类型的所有激活实例共享此元数据。被拒绝的激活请求在可用时复用已缓存的名称，否则为该事件格式化目标 Grain 类型，使缓存增长与类型注册保持关联。工作集插入/移除及激活实例目录注册/移除会更新相应的实例数量。一次指标抓取枚举类型计数器，包括最后一个成员离开后的零计数。不公开 Grain 标识的工作集成员计入无标签实例数量。首次空闲工作集扫描保留成员；逐出时移除成员。已完成的状态转换使按类型和无标签的数量总和与对应本地实例数量一致；并发抓取可能观察到进行中的状态转换。

生命周期计数器记录可累加事件。对同一发出方和时间间隔，将所有类型及无标签贡献的增量相加，即可还原事件总量。回收扫描及额外的回收批次关闭事件保留其批次级作用域；单个激活事件携带各自的 Grain 类型。一次已完成的激活尝试贡献一个带结果及目录标签的延迟观测值；一次已完成的停用贡献一个带关闭路径的观测值。直方图在操作开始时处于启用状态才开始计时。

已构造实例数量存储在相同的按类型元数据中，并在实例分配及释放期间以原子方式更新。管理统计信息读取这些计数器，并在收集快照时合并共享 CLR 实现名称的类型；指标事件保留各自的规范 Grain 类型标识。

实例数量仪表记录快照。要获得集群总量，为每个发出指标的 Silo 及类型选择最新且未过期的观测值，再对这些 Silo 求和。保留不同发出方的标识，使陈旧观测值过期，并在后端将仪表呈现为当前值。反复抓取会产生连续快照。`orleans-grains` 则提供带符号的实例增量，由导出器自收集开始进行聚合。

激活实例目录实例数量、工作集实例数量及已构造实例计数各自保留独立的作用域。一个无状态工作者组贡献一个目录条目，可包含多个已构造工作者。激活失败时，在清理过程中目录条目可能暂时存在。迁移期间，源和目标分别统计自身的注册及移除。

有关 `orleans-grains` 键/值迁移及 Dashboard 更新，请参阅[激活指标架构更新](../../migration-guide.md#activation-metric-schema-update)。

<a id="cluster-manifests"></a>

## 集群清单

| 度量工具 | 类型 | 单位 | 属性 | 描述 |
|---|---|---|---|---|
| `orleans-manifest-cache-lookups` | C | 查找，隐式 | `result`, `source` | 内容缓存查找，结果为 `hit` 或 `miss`，来源为 `silo` 哈希响应或 `peer` 摘要。 |
| `orleans-manifest-fallbacks` | C | 检索，隐式 | `reason` | 回退到直接清单 RPC 的哈希检索：`error`、`missing` 或 `mismatch`。 |
| `orleans-manifest-peer-probes` | C | 尝试，隐式 | `status` | 本地对等探测尝试，以 `success`、`timeout`、`canceled` 或 `error` 完成；`skipped` 统计因本地准入控制被拒绝的尝试。 |
| `orleans-manifest-peer-repairs` | C | Silo 条目，隐式 | - | 成功发布的对等修复所补充的缺失 Silo 清单条目。 |
| `orleans-manifest-retrieval-duration` | H | `ms` | `mode`, `status` | 本地 Silo 清单检索持续时间，模式为 `direct` 或 `hash`，结果为 `success`、`error` 或 `canceled`。 |

这些类别是固定的，不携带 Silo 地址、清单哈希或 Grain 类型名称。探测取消包括被完整直接结果取代的可选工作。有关发布、回退及滚动发布语义，请参阅[集群清单检索](../../implementation/cluster-manifest-retrieval.md)。

<a id="grain-directory-and-consistent-rings"></a>

## Grain 目录和一致性哈希环

| 度量工具 | 类型 | 单位 | 属性 | 描述 |
|---|---|---|---|---|
| `orleans-consistent-ring-range-percentage-average` | OG | 百分比，隐式 | - | 单个 Silo 在一致性哈希环中均分时的百分比（`100 / ring size`）。 |
| `orleans-consistent-ring-range-percentage-local` | OG | 百分比，隐式 | - | 当前由此 Silo 拥有的一致性哈希环百分比。 |
| `orleans-consistent-ring-size` | OG | Silo，隐式 | - | 一致性哈希环中的成员数。 |
| `orleans-directory-cache-size` | OG | 条目，隐式 | - | 所有已注册本地 Grain 目录缓存中的条目总数。 |
| `orleans-directory-lookups-cache-issued` | C | 查找，隐式 | - | 针对此 Silo 目录缓存发起的 Grain 位置查找尝试。 |
| `orleans-directory-lookups-cache-successes` | C | 查找，隐式 | - | 返回可用条目的本地目录缓存查找。 |
| `orleans-directory-lookups-full-issued` | C | 查找，隐式 | - | 此 Silo 发起的完整 Grain 目录查找。 |
| `orleans-directory-lookups-local-directory-issued` | C | 查找，隐式 | - | 直接针对旧版目录分区发起的查找。 |
| `orleans-directory-lookups-local-directory-successes` | C | 查找，隐式 | - | 返回 Grain 地址的旧版目录分区直接查找。 |
| `orleans-directory-lookups-local-issued` | C | 查找，隐式 | - | 通过旧版 Grain 定位器发起的 Grain 位置查找。 |
| `orleans-directory-lookups-local-successes` | C | 查找，隐式 | - | 返回 Grain 地址的旧版 Grain 定位器查找。 |
| `orleans-directory-lookups-remote-received` | C | 查找，隐式 | - | 从其他 Silo 收到的转发目录查找请求。 |
| `orleans-directory-lookups-remote-sent` | C | 查找，隐式 | - | 转发到其他 Silo 的目录查找请求。 |
| `orleans-directory-partition-size` | OG | 条目，隐式 | - | 此 Silo 的旧版目录分区中的 Grain 注册数。 |
| `orleans-directory-range-lock-held-duration` | H | 毫秒，隐式 | - | 目录范围锁持有时长的分布。 |
| `orleans-directory-recovery-count` | C | 恢复，隐式 | - | 成员资格变更后已完成的目录范围恢复次数。 |
| `orleans-directory-recovery-duration` | H | 毫秒，隐式 | - | 已完成的目录范围恢复持续时间的分布。 |
| `orleans-directory-registration-duration` | H | `ms` | `locator`, `status` | Grain 注册持续时间，按定位器实现及 `success`、`canceled` 或 `error` 结果区分。 |
| `orleans-directory-registrations` | C | 注册，隐式 | `locator`, `status` | 已完成的 Grain 注册尝试，按定位器实现及结果区分。 |
| `orleans-directory-registrations-single-act-issued` | C | 注册，隐式 | - | 在此 Silo 上发起的单激活实例注册请求。 |
| `orleans-directory-registrations-single-act-local` | C | 注册，隐式 | - | 由此 Silo 的旧版目录分区处理的单激活实例注册。 |
| `orleans-directory-registrations-single-act-remote-received` | C | 注册，隐式 | - | 从其他 Silo 收到的转发单激活实例注册请求。 |
| `orleans-directory-registrations-single-act-remote-sent` | C | 注册，隐式 | - | 转发到其他 Silo 的单激活实例注册请求。 |
| `orleans-directory-ring-local-portion-average-percentage` | OG | 百分比，隐式 | - | 旧版 Grain 目录环均分时的百分比（`100 / ring size`）。 |
| `orleans-directory-ring-local-portion-distance` | OG | 哈希环距离，隐式 | - | 从此 Silo 到其后继者的原始哈希环距离。 |
| `orleans-directory-ring-local-portion-percentage` | OG | 百分比，隐式 | - | 分配给此 Silo 的旧版 Grain 目录环百分比。 |
| `orleans-directory-ring-size` | OG | Silo，隐式 | - | 旧版 Grain 目录环中的成员数。 |
| `orleans-directory-snapshot-transfer-count` | C | 传输，隐式 | - | 从先前分区所有者成功接收的目录范围快照。 |
| `orleans-directory-snapshot-transfer-duration` | H | 毫秒，隐式 | - | 成功传输目录快照的持续时间分布。 |
| `orleans-directory-unregistrations-issued` | C | 注销，隐式 | - | 在此 Silo 上发起的单个 Grain 注销请求。 |
| `orleans-directory-unregistrations-local` | C | 注销，隐式 | - | 由此 Silo 的旧版目录分区处理的单个 Grain 注销请求。 |
| `orleans-directory-unregistrations-many-issued` | C | 请求，隐式 | - | 在此 Silo 上发起的批量 Grain 注销请求。 |
| `orleans-directory-unregistrations-many-remote-received` | C | 请求，隐式 | - | 从其他 Silo 收到的转发批量注销请求。 |
| `orleans-directory-unregistrations-many-remote-sent` | C | 请求，隐式 | - | 转发到其他 Silo 的批量注销请求。 |
| `orleans-directory-unregistrations-remote-received` | C | 注销，隐式 | - | 从其他 Silo 收到的转发单个注销请求。 |
| `orleans-directory-unregistrations-remote-sent` | C | 注销，隐式 | - | 转发到其他 Silo 的单个注销请求。 |
| `orleans-directory-validations-cache-received` | C | 请求，隐式 | - | 从其他 Silo 收到的批量目录缓存验证请求。 |

<a id="reminders-storage-runtime-resources-and-watchdog"></a>

## 提醒器、存储、运行时资源及看门狗

| 度量工具 | 类型 | 单位 | 属性 | 描述 |
|---|---|---|---|---|
| `orleans-reminders-active` | OG | 提醒器，隐式 | - | 本地跟踪的提醒器数量上限，包括临时墓碑记录。 |
| `orleans-reminders-tardiness` | H | `seconds` | - | 提醒器计划触发时间与开始投递时间之间的延迟。 |
| `orleans-reminders-ticks-delivered` | C | 触发次数，隐式 | - | 成功投递到 `IRemindable.ReceiveReminder` 的提醒器触发事件。 |
| `orleans-runtime-available-memory` | OG | `MB` | - | GC 报告的可用内存预算，除以 1,024 的平方。 |
| `orleans-runtime-total-physical-memory` | OG | `MB` | - | <xref:System.GCMemoryInfo.TotalAvailableMemoryBytes> 除以 1,024 的平方。此历史名称可能表示进程或容器内存限制，而非主机物理 RAM。 |
| `orleans-storage-clear-errors` | C | 错误，隐式 | `provider_type_name`, `state_name`, `state_type` | 抛出异常的 Grain 状态清除操作。 |
| `orleans-storage-clear-latency` | H | `ms` | `provider_type_name`, `state_name`, `state_type` | 成功完成的 Grain 状态清除操作延迟。 |
| `orleans-storage-read-errors` | C | 错误，隐式 | `provider_type_name`, `state_name`, `state_type` | 抛出异常的 Grain 状态读取操作。 |
| `orleans-storage-read-latency` | H | `ms` | `provider_type_name`, `state_name`, `state_type` | 成功完成的 Grain 状态读取延迟。 |
| `orleans-storage-write-errors` | C | 错误，隐式 | `provider_type_name`, `state_name`, `state_type` | 抛出异常的 Grain 状态写入操作。 |
| `orleans-storage-write-latency` | H | `ms` | `provider_type_name`, `state_name`, `state_type` | 成功完成的 Grain 状态写入延迟。 |
| `orleans-watchdog-health-checks` | C | 检查，隐式 | - | 已执行的运行时看门狗健康检查次数。 |
| `orleans-watchdog-health-checks-failed` | C | 检查，隐式 | - | 报告失败的运行时看门狗健康检查次数。 |

<a id="streaming"></a>

## 流式处理

发布订阅度量工具使用 `provider` 和 `grain_type`。持久化流读取度量工具使用 `provider` 和 `queue`；发送度量工具使用 `provider` 和 `grain_type`。默认接收器及缓存监视器使用 `QueueId`，默认块池监视器使用 `BlockPoolId`。自定义监视器实现可选择不同的维度。

| 度量工具 | 类型 | 单位 | 属性 | 描述 |
|---|---|---|---|---|
| `orleans-streams-block-pool-allocated-memory` | OC | `bytes` | 默认： `BlockPoolId` | 从流缓冲池分配的累计字节数。 |
| `orleans-streams-block-pool-available-memory` | OC | `bytes` | 默认： `BlockPoolId` | 流缓冲池报告的当前可用字节数。此时间点值以可观测计数器实现。 |
| `orleans-streams-block-pool-claimed-memory` | OC | `bytes` | 默认： `BlockPoolId` | 流缓冲池报告的当前已占用字节数。此时间点值以可观测计数器实现。 |
| `orleans-streams-block-pool-released-memory` | OC | `bytes` | 默认： `BlockPoolId` | 释放回流缓冲池的累计字节数。 |
| `orleans-streams-block-pool-total-memory` | OC | `bytes` | 默认： `BlockPoolId` | 流缓冲池报告的当前总字节数。此时间点值以可观测计数器实现。 |
| `orleans-streams-persistent-stream-messages-read` | C | 批次容器，隐式 | `provider`, `queue` | 从持久化流接收器队列读取的批次容器。一个容器可包含多个流事件。 |
| `orleans-streams-persistent-stream-messages-sent` | C | 批次容器，隐式 | `provider`, `grain_type` | 被选中向消费者投递的批次容器。计数器在投递之前递增，因此不是成功投递次数。 |
| `orleans-streams-persistent-stream-pubsub-cache-size` | OG | 条目，隐式 | `name` | 持久化流拉取代理持有的发布订阅缓存条目。 |
| `orleans-streams-persistent-stream-pulling-agents` | OG | 代理，隐式 | `name` | 此 Silo 上持久化流提供程序当前拥有的拉取代理数。 |
| `orleans-streams-pubsub-consumers` | C，带符号增量 | 消费者，隐式 | `provider`, `grain_type` | 此进程记录的消费者注册带符号累计增量，增加为正值、移除为负值。该时间序列不从持久化发布订阅状态初始化，也不是时间点计数。 |
| `orleans-streams-pubsub-consumers-added` | C | 尝试，隐式 | `provider`, `grain_type` | 发布订阅消费者注册尝试。 |
| `orleans-streams-pubsub-consumers-removed` | C | 尝试，隐式 | `provider`, `grain_type` | 发布订阅消费者注销尝试。 |
| `orleans-streams-pubsub-producers` | C，带符号增量 | 生产者，隐式 | `provider`, `grain_type` | 此进程记录的生产者注册带符号累计增量，增加为正值、移除为负值。该时间序列不从持久化发布订阅状态初始化，也不是时间点计数。 |
| `orleans-streams-pubsub-producers-added` | C | 尝试，隐式 | `provider`, `grain_type` | 发布订阅生产者注册尝试。 |
| `orleans-streams-pubsub-producers-removed` | C | 尝试，隐式 | `provider`, `grain_type` | 发布订阅生产者注销尝试。 |
| `orleans-streams-queue-cache-length` | OC | `messages` | 默认： `QueueId` | 队列缓存中的当前消息数。此时间点值以可观测计数器实现。 |
| `orleans-streams-queue-cache-memory-allocated` | OC | 字节，隐式 | 默认： `QueueId` | 队列缓存分配的累计字节数。 |
| `orleans-streams-queue-cache-memory-released` | OC | 字节，隐式 | 默认： `QueueId` | 队列缓存释放的累计字节数。 |
| `orleans-streams-queue-cache-messages-added` | OC | 消息，隐式 | 默认： `QueueId` | 添加到队列缓存的累计消息数。 |
| `orleans-streams-queue-cache-messages-purged` | OC | 消息，隐式 | 默认： `QueueId` | 从队列缓存清除的累计消息数。 |
| `orleans-streams-queue-cache-oldest-age` | OG | `TimeSpan` 时钟周期，隐式 | 默认： `QueueId` | 缓存监视器仍表示的最早已出队消息的存续时长。 |
| `orleans-streams-queue-cache-oldest-to-newest-duration` | OG | `TimeSpan` 时钟周期，隐式 | 默认： `QueueId` | 缓存监视器表示的最早消息到最新消息的入队时间跨度。 |
| `orleans-streams-queue-cache-pressure` | OG | 比率，隐式 | 默认： `QueueId`, `PressureMonitorType` | 每个缓存压力监视器报告的当前压力值。 |
| `orleans-streams-queue-cache-pressure-contribution-count` | OG | 贡献，隐式 | 默认： `QueueId`, `PressureMonitorType` | 每个缓存压力监视器报告的压力贡献次数。 |
| `orleans-streams-queue-cache-size` | OC | `bytes` | 默认： `QueueId` | 当前队列缓存大小。此时间点值以可观测计数器实现。 |
| `orleans-streams-queue-cache-under-pressure` | OG | 布尔值（`0` 或 `1`），隐式 | 默认： `QueueId`, `PressureMonitorType` | 每个缓存压力监视器当前是否报告存在压力。 |
| `orleans-streams-queue-initialization-duration` | C | `TimeSpan` 时钟周期，隐式 | 默认： `QueueId` | 队列接收器初始化调用的累计持续时间。 |
| `orleans-streams-queue-initialization-exceptions` | C | 异常，隐式 | 默认： `QueueId` | 提供异常的队列接收器初始化调用。 |
| `orleans-streams-queue-initialization-failures` | C | 失败，隐式 | 默认： `QueueId` | 报告为不成功的队列接收器初始化调用。 |
| `orleans-streams-queue-messages-received` | OC | 消息，隐式 | 默认： `QueueId` | 从流队列收到的累计消息数。 |
| `orleans-streams-queue-newest-message-enqueue-age` | OG | `TimeSpan` 时钟周期，隐式 | 默认： `QueueId` | 最近一次队列读取返回的最新消息的入队距今时长。 |
| `orleans-streams-queue-oldest-message-enqueue-age` | OG | `TimeSpan` 时钟周期，隐式 | 默认： `QueueId` | 最近一次队列读取返回的最早消息的入队距今时长。 |
| `orleans-streams-queue-read-duration` | C | `TimeSpan` 时钟周期，隐式 | 默认： `QueueId` | 队列接收器读取调用的累计持续时间。 |
| `orleans-streams-queue-read-exceptions` | C | 异常，隐式 | 默认： `QueueId` | 提供异常的队列接收器读取调用。 |
| `orleans-streams-queue-read-failures` | C | 失败，隐式 | 默认： `QueueId` | 报告为不成功的队列接收器读取调用。 |
| `orleans-streams-queue-shutdown-duration` | C | `TimeSpan` 时钟周期，隐式 | 默认： `QueueId` | 队列接收器关闭调用的累计持续时间。 |
| `orleans-streams-queue-shutdown-exceptions` | C | 异常，隐式 | 默认： `QueueId` | 提供异常的队列接收器关闭调用。 |
| `orleans-streams-queue-shutdown-failures` | C | 失败，隐式 | 默认： `QueueId` | 报告为不成功的队列接收器关闭调用。 |

对于同一个操作，失败计数器与异常计数器均可能递增。持续时间计数器聚合经过的总时钟周期数；计算时间窗口内的平均值时，用其增量除以对应的操作计数增量。

<a id="transactions"></a>

## 事务

| 度量工具 | 类型 | 单位 | 属性 | 描述 |
|---|---|---|---|---|
| `orleans-transactions-failed` | OC | 事务，隐式 | - | 未成功完成的事务。 |
| `orleans-transactions-started` | OC | 事务，隐式 | - | 由本地事务代理启动的事务。 |
| `orleans-transactions-successful` | OC | 事务，隐式 | - | 成功完成的事务。 |
| `orleans-transactions-throttled` | OC | 事务，隐式 | - | 因事务代理限流而被拒绝的事务尝试。 |

<a id="durable-jobs"></a>

## 持久化作业

| 度量工具 | 类型 | 单位 | 属性 | 描述 |
|---|---|---|---|---|
| `orleans-durablejobs-cancel-job-call-duration` | H | `ms` | `status` | `CancelAsync` 调用持续时间，按 `cancellation_requested`、`not_found`、`operation_canceled` 或 `error` 区分。 |
| `orleans-durablejobs-cancel-job-calls` | C | 调用，隐式 | `status` | 已完成的 `CancelAsync` 调用，按请求结果区分。 |
| `orleans-durablejobs-handler-execution-duration` | H | `ms` | `status` | 已完成处理程序执行的持续时间，按 `completed`、`attempt_canceled` 或 `failed` 区分。 |
| `orleans-durablejobs-handler-executions` | C | 执行，隐式 | `status` | 已达到终止结果的处理程序执行。 |
| `orleans-durablejobs-handler-executions-started` | C | 执行，隐式 | - | 已开始的持久化作业处理程序执行。 |
| `orleans-durablejobs-job-attempt-duration` | H | `ms` | `status` | 作业尝试持续时间，按 `completed`、`failed`、`retried` 或 `rescheduled` 结果区分。 |
| `orleans-durablejobs-job-attempts-started` | C | 尝试，隐式 | - | 已开始的持久化作业尝试。 |
| `orleans-durablejobs-job-dispatch-lag` | H | `ms` | - | 作业到期时间与其尝试开始时间之间的延迟。 |
| `orleans-durablejobs-job-schedule-duration` | H | `ms` | - | 持久化并调度作业所需的时间。 |
| `orleans-durablejobs-job-cancellation-requests` | C | 请求，隐式 | - | 已持久化记录的持久化作业取消请求。 |
| `orleans-durablejobs-jobs-completed` | C | 作业，隐式 | - | 成功完成的持久化作业。 |
| `orleans-durablejobs-jobs-failed` | C | 作业，隐式 | - | 已记录为失败的持久化作业尝试。 |
| `orleans-durablejobs-jobs-retried` | C | 作业，隐式 | - | 已安排重试的持久化作业尝试。 |
| `orleans-durablejobs-jobs-scheduled` | C | 作业，隐式 | - | 成功调度的持久化作业。 |
| `orleans-durablejobs-ownership-check-duration` | H | `ms` | - | 分片所有权检查持续时间。 |
| `orleans-durablejobs-schedule-job-call-duration` | H | `ms` | `status` | 调度作业 API 调用持续时间，按 `ok`、`operation_canceled` 或 `error` 区分。 |
| `orleans-durablejobs-schedule-job-calls` | C | 调用，隐式 | `status` | 已完成的调度作业 API 调用，按结果区分。 |
| `orleans-durablejobs-shard-batch-bytes` | H | `bytes` | - | 持久化作业分片变更批次序列化后的字节大小。 |
| `orleans-durablejobs-shard-batch-mutations` | H | 变更，隐式 | - | 持久化作业分片批次中的变更数量。 |
| `orleans-durablejobs-shard-pending-depth` | H | 变更，隐式 | - | 处理前立即收集到每个分片批次中的待处理变更操作数量。 |
| `orleans-durablejobs-shard-processing-duration` | H | `ms` | `status` | 分片处理持续时间，按 `completed`、`attempt_canceled` 或 `error` 区分。 |
| `orleans-durablejobs-shards-processed` | C | 分片，隐式 | `status` | 已完成的分片处理轮次，按结果区分。 |
| `orleans-durablejobs-storage-batch-size` | H | 变更，隐式 | `status` | 每次状态写入尝试所包含的已应用分片变更数量，按结果区分。 |
| `orleans-durablejobs-storage-batches` | C | 批次，隐式 | `status` | 持久化作业存储批次，按 `ok`、`operation_canceled` 或 `error` 区分。 |
| `orleans-durablejobs-stripe-distribution` | C | 分配，隐式 | `stripe` | 按持久化作业条带编号区分的作业分配。 |

<a id="journaling"></a>

## 日志化

核心日志化度量工具在适用时使用 `operation` 和 `status`。操作值包括 `append`、`snapshot`、`replace`、`read`、`delete` 和 `recovery`；状态为 `ok` 或 `error`。

| 度量工具 | 类型 | 单位 | 属性 | 描述 |
|---|---|---|---|---|
| `orleans-journaling-compaction-triggers` | C | 触发，隐式 | `reason` | 因 `user_snapshot`、`storage_requested` 或 `migration` 请求的日志压实。 |
| `orleans-journaling-gather-duration` | H | `ms` | `operation` | 为存储操作收集日志化状态所用的时间。 |
| `orleans-journaling-recoveries` | C | 恢复，隐式 | `operation`, `status` | 日志恢复尝试，按结果区分。 |
| `orleans-journaling-recovery-duration` | H | `ms` | `operation`, `status` | 日志恢复持续时间，按结果区分。 |
| `orleans-journaling-state-delete-duration` | H | `ms` | `operation`, `status` | 日志化状态删除请求的持续时间。 |
| `orleans-journaling-state-delete-requests` | C | 请求，隐式 | `operation`, `status` | 日志化状态删除请求，按结果区分。 |
| `orleans-journaling-state-scan-count` | H | 状态，隐式 | `operation` | 为操作收集工作时扫描的日志化状态数量。 |
| `orleans-journaling-state-write-duration` | H | `ms` | `operation`, `status` | 日志化状态写入请求的持续时间。 |
| `orleans-journaling-state-write-requests` | C | 请求，隐式 | `operation`, `status` | 日志化状态写入请求，按操作及结果区分。 |
| `orleans-journaling-storage-bytes` | C | `bytes` | `operation` | 日志存储操作成功处理的累计字节数。 |
| `orleans-journaling-storage-operation-bytes` | H | `bytes` | `operation`, `status` | 成功的日志存储操作大小分布。 |
| `orleans-journaling-storage-operation-duration` | H | `ms` | `operation`, `status` | 日志存储操作持续时间，按操作及结果区分。 |
| `orleans-journaling-storage-operation-queue-duration` | H | `ms` | `operation`, `status` | 日志存储操作在执行之前的排队时长。 |
| `orleans-journaling-storage-operations` | C | 操作，隐式 | `operation`, `status` | 日志存储操作，按操作及结果区分。 |
| `orleans-journaling-write-coalesced-callers` | H | 调用方，隐式 | `operation` | 每次合并日志写入所合并的调用方数量。 |

<a id="provider-workload-counters"></a>

### 提供程序工作负载计数器

提供程序计数器在现有执行点记录目录遍历和显式提供程序重试。
每个提供程序库为 `provider` 标签定义自己的短名称：`s3`、
`azure_blob`、`azure_table`、`redis` 或 `volatile`。现有状态管理器及提供程序特定的
度量工具提供存储操作延迟、结果及字节数。

| 度量工具 | 类型 | 单位 | 属性 | 描述 |
|---|---|---|---|---|
| `orleans-journaling-provider-catalog-entries` | C | 条目，隐式 | `provider` | 投递给消费者的目录条目。 |
| `orleans-journaling-provider-catalog-pages` | C | 页，隐式 | `provider` | S3 和 Azure 目录遍历期间收到的页数，包括空页。 |
| `orleans-journaling-provider-catalog-items` | C | 项，隐式 | `provider` | 筛选前的原生目录页候选项，或目录遍历过程中使用的 Redis 扫描键。 |
| `orleans-journaling-provider-retries` | C | 重试，隐式 | `provider`, `reason` | 显式提供程序循环重试次数，使用有界的原因标签。 |

S3 和 Azure 对目录遍历收到的每一页及该页中的所有候选项计数，
计数发生在本地筛选之前。Redis 对遍历消耗的键计数，计数发生在筛选和去重之前。
易失性存储记录已投递的条目。比较候选项及已投递条目，
可以了解提供程序筛选了多少数据。

空的逻辑范围产生零页、零项和零条目。提前释放及取消
保留已经记录的计数。到达枚举末尾的一次推进或检索页面失败会增加
零页。提供程序重试计数描述显式存储恢复循环。

使用现有持续时间直方图分析存储延迟，并使用
[主机提供的依赖项遥测](../../grains/journaling/operations.md#use-host-provided-dependency-telemetry)
分析 SDK 请求时序、结果及传输尝试。目录页及项计数描述
提供程序遍历行为；服务端事务指标是计费对账的依据。

下方现有的状态管理器及提供程序特定度量工具保留原有含义。

Azure 和 S3 存储方法度量工具使用操作 `create`、`get_metadata`、`update_metadata`、`append`、`delete`、`read` 和 `replace`。

| 度量工具 | 类型 | 单位 | 属性 | 描述 |
|---|---|---|---|---|
| `orleans-journaling-azure-blob-operation-bytes` | C | `bytes` | `operation` | Azure Blob 日志存储操作成功处理的累计字节数。 |
| `orleans-journaling-azure-blob-operation-duration` | H | `ms` | `operation`, `status` | Azure Blob 日志存储操作持续时间，按操作及结果区分。 |
| `orleans-journaling-azure-blob-operations` | C | 操作，隐式 | `operation`, `status` | Azure Blob 日志存储操作，按操作及 `ok` 或 `error` 状态区分。 |
| `orleans-journaling-azure-table-operation-bytes` | C | `bytes` | `operation` | Azure Table 日志存储操作成功处理的累计字节数。 |
| `orleans-journaling-azure-table-operation-duration` | H | `ms` | `operation`, `status` | Azure Table 日志存储操作持续时间，按操作及结果区分。 |
| `orleans-journaling-azure-table-operations` | C | 操作，隐式 | `operation`, `status` | Azure Table 日志存储操作，按操作及 `ok` 或 `error` 状态区分。 |
| `orleans-journaling-s3-operation-bytes` | C | `bytes` | `operation` | S3 日志存储方法成功处理的累计字节数。 |
| `orleans-journaling-s3-operation-duration` | H | `ms` | `operation`, `status` | S3 日志存储方法持续时间，按操作及结果区分。 |
| `orleans-journaling-s3-operations` | C | 操作，隐式 | `operation`, `status` | S3 日志存储方法，按操作及 `ok` 或 `error` 状态区分。 |

<a id="use-the-catalog"></a>

## 使用此目录

使用[监视 Orleans 指标](metrics.md)选择健康指示信号，按照各度量工具类型聚合、控制基数及构建告警。使用 [Orleans 事件故障排查](troubleshooting.md)，将指标与日志、跟踪记录、部署变更及依赖项健康状况关联。
