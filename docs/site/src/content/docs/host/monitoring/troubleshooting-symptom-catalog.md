---
title: Orleans 症状与信号目录
description: 根据异常、日志事件、指标或观测到的行为查找 Orleans 生产环境问题的解决措施。
ms.date: 08/11/2026
ms.topic: troubleshooting
---

<a id="orleans-symptom-and-signal-catalog"></a>

# Orleans 症状与信号目录

当你掌握某个具体异常、事件 ID、指标或观测到的行为时，请使用此目录。在本页搜索信号的原始文本，再通过相应链接查看完整调查步骤。

异常或事件标识 Orleans 在何处发现问题。保留异常链、结构化的 <xref:Microsoft.Extensions.Logging.EventId>、类别、跟踪 ID、Silo 标识、集群成员资格视图及首次发生时间，然后将这些证据关联起来确定根本原因。

<a id="quick-index"></a>

## 快速索引

| 信号 | 首先参阅 |
|---|---|
| <xref:Orleans.Runtime.Messaging.ConnectionFailedException>，没有已连接网关 | [连接与网关故障](#connection-and-gateway-failures) |
| <xref:Orleans.Runtime.SiloUnavailableException>、<xref:Orleans.Runtime.OrleansMessageRejectionException> | [Silo 不可用或消息被拒绝](#silo-unavailable-or-message-rejected) |
| <xref:Orleans.Runtime.GatewayTooBusyException>，消息被拒绝或因负载削减而未处理 | [网关或集群过载](#gateway-or-cluster-overload) |
| <xref:System.TimeoutException>、`orleans-app-requests-timedout` | [请求超时](#request-timeouts) |
| `orleans-scheduler-long-running-turns`，对某个 Grain 的调用停止推进 | [长时间运行或死锁的 Grain 执行轮次](#long-running-or-deadlocked-grain-turns) |
| <xref:Orleans.Storage.InconsistentStateException>，ETag 或版本冲突 | [存储一致性故障](#storage-consistency-failures) |
| 存储错误或延迟随后端限流而增加 | [存储限流](#storage-throttling) |
| 部署后出现序列化器或编解码器异常 | [版本变更后的序列化故障](#serialization-failures-after-a-version-change) |
| 成员资格反复变更、探测回复缺失、重复激活 | [成员资格与目录频繁变动](#membership-and-directory-churn) |
| 集群仍有剩余容量，但某个 Grain 响应缓慢 | [热点 Grain 与放置偏斜](#hot-grains-and-skewed-placement) |
| 提醒器或定时器运行迟到、运行两次，或遗漏预期运行 | [提醒器与定时器时序](#reminder-and-timer-timing) |
| 可用内存下降、GC 暂停增加，或容器因 OOM 被终止 | [内存压力](#memory-pressure) |
| 关闭超过终止宽限期 | [关闭超过终止时间预算](#shutdown-exceeds-the-termination-budget) |

<a id="connection-and-gateway-failures"></a>

## 连接与网关故障

**信号：** <xref:Orleans.Runtime.Messaging.ConnectionFailedException>、反复尝试连接、`orleans-client-connected-gateways` 为 0，或客户端就绪状态一直为 false。

**可能原因：** 客户端与 Silo 的集群标识不同；网关提供程序返回空的活动 Silo 集合；通告的网关地址解析到客户端路由范围之外；DNS、防火墙、NAT 或网络策略阻断流量；或者 TLS 名称、信任配置或客户端证书要求不同。

**确认：** 从客户端网络查询网关信息源，比较集群 ID 和服务 ID，并测试每个通告网关的 DNS 及 TCP 连通性。对于 TLS，保留内部握手异常，并检查证书 SAN、信任链、EKU 和 `TargetHost`。

**解决：** 修正标识/提供程序配置，或发布客户端可路由到的地址。替换无效证书或信任配置。立即暴露确定性配置错误，并为启动重试设置上限。

**预防：** 从每个部署网络验证确切的通告终结点，并在客户端拥有可用网关后报告就绪状态。遵循[客户端连接故障](troubleshooting.md#client-cant-connect)中的指导。

<a id="silo-unavailable-or-message-rejected"></a>

## Silo 不可用或消息被拒绝

**信号：** <xref:Orleans.Runtime.SiloUnavailableException>、<xref:Orleans.Runtime.OrleansMessageRejectionException>、连接关闭，或者故障集中发生在成员资格变更前后。

**可能原因：** Silo 停止或网络不可达；成员资格正在收敛；频繁变动期间使用了过期激活地址；目标正在关闭；或过载导致拒绝。

**确认：** 将异常时间戳及目标 Silo 与成员资格、部署、套接字、探测及进程事件关联。通过跟踪记录或应用程序操作 ID 确定调用是否到达应用程序代码。

**解决：** 恢复集群/网络健康或完成滚动发布。Grain 引用对于后续调用仍然有效。由于失败调用的结果可能不确定，仅对具有幂等性或去重机制的操作进行有界重试。

**预防：** 滚动发布期间保留故障域容量，使可重试操作具备幂等性，并在负载下测试成员丢失。请参阅[成员资格变更期间调用失败](../../deployment/troubleshooting-deployments.md#calls-fail-during-membership-changes)。

<a id="gateway-or-cluster-overload"></a>

## 网关或集群过载

**信号：** <xref:Orleans.Runtime.GatewayTooBusyException>、`Shedding load` 拒绝、`orleans-messaging-rejected` 或 `orleans-gateway-load-shedding` 增长、排队延迟增加，或者需求增加但吞吐量下降。

**可能原因：** 已启用网关负载削减且 CPU 或内存超过配置阈值；Grain 调度器饱和；存在热点 Grain；线程池或 CPU 饥饿；或者存储/流依赖项的处理速率低于输入速率。

**确认：** 将进入、完成、拒绝及超时速率与 CPU、线程池队列、长时间运行的执行轮次、激活实例数量及依赖项延迟比较。比较各 Silo：单个异常实例可能表明偏斜或主机问题；集群整体变化可能表明共享容量或依赖项压力。

**解决：** 首先减少准入流量和可选工作。在安全的重试边界使用带随机抖动的有界指数退避。仅当工作可以分区且共享依赖项具有剩余容量时才进行扩容。

**预防：** 对真实键分布和扇出分布进行负载测试，在开始拒绝之前告警，并明确网关及依赖项的容量范围。遵循[集群过载](troubleshooting.md#the-cluster-is-overloaded)中的指导。

<a id="request-timeouts"></a>

## 请求超时

**信号：** <xref:System.TimeoutException>、`orleans-app-requests-timedout`（带 `grain_type` 标签）增长，或 `orleans-app-requests-latency` 直方图中的分桶计数或百分位数上升。

**可能原因：** 执行前排队、长时间运行的 Grain 执行轮次、循环调用、阻塞工作、缓慢的存储或其他依赖项、成员资格恢复或网络丢包。

**确认：** 追踪采样的跟踪记录，确定调用是否到达 Silo 及等待发生的位置。将超时与长时间运行的执行轮次、拒绝/丢弃的消息、CPU、GC、线程池、成员资格及依赖项延迟关联。

**解决：** 移除瓶颈或减少负载。当测得的正常延迟超过时间预算时，增加超时时间。将超时操作视为结果不确定，并协调修正应用程序状态。

**预防：** 设置端到端延迟预算，限制依赖项调用及队列，在安全的情况下传播取消，并为重试使用幂等性或去重机制。遵循 [Grain 调用超时](troubleshooting.md#grain-calls-time-out)中的指导。

<a id="long-running-or-deadlocked-grain-turns"></a>

## 长时间运行或死锁的 Grain 执行轮次

**信号：** `orleans-scheduler-long-running-turns` 增长；对某个激活实例的调用停止推进；跟踪记录显示应用程序执行耗时较长；或者调用栈显示 `.Result`、`.Wait()`、监视器锁争用或 Grain 循环调用。

**可能原因：** 同步阻塞、CPU 密集型工作、无界循环、等待无界或延迟超过请求预算的外部 I/O，或者 Grain 之间的调用环。可重入性可以使调用环继续推进，而同步阻塞仍会占用调度器。

**确认：** 在问题仍存在时捕获跟踪记录或转储。确定 Grain 类型和方法，检查托管调用栈及调度器队列，并绘制调用图。检查不可重入激活实例是否正在等待最终会回调自身的调用。

**解决：** 移除阻塞等待；使用有界异步操作；打破调用环；将 CPU 密集型工作移出 Grain 执行轮次；或重新设计协议。对于已知必须重新进入发起者的调用路径，将 <xref:Orleans.Runtime.RequestContext.AllowCallChainReentrancy*> 的作用域限定为该调用链。只有在证明交错执行的状态转换安全之后，才能使用 <xref:Orleans.Concurrency.ReentrantAttribute> 或 <xref:Orleans.Concurrency.AlwaysInterleaveAttribute>。请参阅[调用链可重入性](../../grains/request-scheduling.md#call-chain-reentrancy)。

**预防：** 限制执行轮次，审查 Grain 间调用图，测试回调路径，并记录交错执行下的 Grain 不变量。遵循 [Grain 执行轮次似乎卡住](troubleshooting.md#grain-turns-appear-stuck)中的指导。

<a id="storage-consistency-failures"></a>

## 存储一致性故障

**信号：** <xref:Orleans.Storage.InconsistentStateException>、ETag/版本/前置条件冲突，或反复无法写入 Grain 状态。

**可能原因：** 并发写入者、成员资格或自定义目录故障期间的重复激活、应用程序代码复用过期状态，或者绕过正常写入路径的外部写入者修改同一记录。

**确认：** 保留已存储和预期的版本值、Grain 类型/键、提供程序操作及成员资格时间线。确定是否有多个激活实例或外部进程写入记录。记录标识符和版本，同时对 Grain 状态及机密保持脱敏。

**解决：** 停止不安全的写入者，并按照应用程序语义协调修正记录。仅针对瞬态冲突进行有界重试，并保留较新的记录。如果 <xref:Orleans.Storage.InconsistentStateException> 从引发它的激活实例传播出去，Orleans 会停用该激活实例，以便后续调用重新加载状态。

**预防：** 每个状态记录保持一个权威写入者，使用提供程序并发令牌，通过该写入者路由更新，并测试从结果不确定的写入及激活实例频繁变动中恢复。

<a id="storage-throttling"></a>

## 存储限流

**信号：** `orleans-storage-read-errors`、`orleans-storage-write-errors` 或 `orleans-storage-clear-errors` 增加；相应的 `orleans-storage-*-latency` 度量工具显示延迟增加；提供程序异常报告限流或速率限制；随后出现请求超时。

**可能原因：** 预配吞吐量过低；分区键偏斜；连接池耗尽；重试放大负载；或者集中激活/恢复造成突发流量。

**确认：** 将提供程序请求单位、分区限流、延迟和重试量与 Orleans 存储操作及激活实例数量关联。通过获准的低基数分区分类比较受影响的 Grain 键，不要将原始 Grain 键纳入指标。

**解决：** 降低准入流量及重试放大效应，增加或重新分配后端容量，并缓解分区热点。仅使用提供程序文档说明的瞬态故障策略，并设置有界重试预算。

**预防：** 使用真实的状态大小和键分布进行存储容量测试，限制重试预算，在限流传播为调用超时之前针对延迟告警，并错开恢复工作。

<a id="serialization-failures-after-a-version-change"></a>

## 版本变更后的序列化故障

**信号：** <xref:Orleans.Serialization.CodecNotFoundException> 报告 `Could not find a codec for type ...` 或 `Could not find a copier for type ...`；其他序列化异常报告不受支持的传输格式类型或未知/必需的类型或字段；`KeyNotFoundException` 报告 `Could not find a base type serializer for type ...` 或 `Could not find a value serializer for type ...`；或者部署后激活/存储读取开始失败。

**可能原因：** 混合版本使用不兼容的契约或序列化器标识；可序列化类型或成员标识发生变化；成员 ID 被复用或重新编号；重命名的类型缺少稳定别名；必需的序列化器注册不同；或者已持久化/排队的数据包含旧表示形式。

**确认：** 捕获完整异常和类型名称，确定有效载荷来自调用、存储、流、提醒器还是队列，并比较新旧版本生成的序列化元数据及包版本。在更改生产数据前，使用新版本读取具有代表性的旧数据以复现问题。

**解决：** 回滚至兼容版本，或部署可理解两种表示形式的读取器，然后有计划地迁移数据。在确定恢复及保留要求之前，保留无法读取的状态及队列。

**预防：** 保持 `[Id(n)]` 值及类型别名稳定，测试滚动升级中两个版本方向的兼容性，并将已持久化及排队的有效载荷纳入兼容性测试。请参阅[部署与回滚](../../migration/deployment-and-rollback.md)。

<a id="membership-and-directory-churn"></a>

## 成员资格与目录频繁变动

**信号：** Silo 反复离开 `Active` 状态、探测回复缺失、成员资格警告、瞬态重复激活、反复地址失效，或者调用在新旧激活地址之间来回转发。

**可能原因：** 进程或网络暂停、CPU/GC 资源饥饿、成员资格存储延迟或限流、通告地址不稳定、时钟问题、过于激进的滚动发布、网络分区，或者自定义 Grain 目录的一致性/清理行为。

**确认：** 将所有 Silo 的成员资格视图和时间戳与平台事件、提供程序延迟、探测及通告终结点比较。对于疑似脑裂，确定被隔离的 Silo 是否保持存活并能够访问成员资格存储。必须具备相互印证的成员资格、提供程序、网络及平台证据，才能诊断为网络分区。

**解决：** 停止滚动发布/缩容，保留较大的健康分区，恢复成员资格存储及网络健康，并等待收敛。在更改成员资格记录前隔离过期分区：正在运行的 Silo 如果读取到自身成员资格条目为 `Dead`，会自行终止。对于自定义目录，遵循其文档说明的修复流程。

**预防：** 使用共享持久化成员资格提供程序；保持终结点稳定且相互可达；维持时钟同步及故障域容量；并测试网络分区与滚动替换。遵循[成员资格不稳定](troubleshooting.md#membership-is-unstable)及[灾难恢复](../../deployment/disaster-recovery.md)中的指导。

<a id="hot-grains-and-skewed-placement"></a>

## 热点 Grain 与放置偏斜

**信号：** 某个 Grain 类型/键排队严重或延迟较高，而其他 Silo 仍有剩余容量；少量 Silo 的 CPU 使用率或激活实例数量不成比例；或者增加 Silo 时吞吐量保持不变。

**可能原因：** 一个一致性边界接收了大部分流量、扇入集中于一个聚合器、键分布不均匀、资源约束放置的合格 Silo 太少，或者放置机制优化本地性却使工作集中。

**确认：** 按有限范围的 Grain 类型/操作维度比较延迟及吞吐量，检查有代表性的跟踪记录和 Dashboard 方法性能分析，并以安全方式分析应用程序键分布。区分单个热点激活实例与普遍开销较大的 Grain 类型。

**解决：** 将领域工作负载划分到多个键上，采用分层聚合或批处理，对合适的无状态工作使用无状态工作者，或放宽不必要的限制性放置规则。普通有状态激活实例在迁移后仍保留单执行轮次的并发约束；对键分区可增加并行度。

**预防：** 对偏斜流量及超热点键流量进行负载测试，定义每键限制，通过获准的低基数分类监视放置/激活分布，并保护原始 Grain 键的隐私。请参阅[如何避免热点 Grain？](../../resources/frequently-asked-questions.md#how-do-i-avoid-hot-grains)。

<a id="reminder-and-timer-timing"></a>

## 提醒器与定时器时序

**信号：** `orleans-reminders-tardiness` 增长、`orleans-reminders-ticks-delivered` 停止推进、回调执行迟到、暂停后回调集中触发、提醒器似乎运行多次，或者停用/重启后回调停止。

**可能原因：** 错误地预期定时器回调在激活实例丢失后继续存在；提醒器提供程序或成员资格依赖项不可用；调度器/CPU 压力延迟执行；回调持续时间超过周期；或者应用程序代码假定恰好一次投递。

**确认：** 首先确定应用程序使用 Grain 定时器还是提醒器。将回调时间戳与激活实例生命周期、Silo 重启、调度器延迟、成员资格、提醒器提供程序健康状况及回调持续时间关联。使用持久化应用程序操作 ID 检测重复工作。

**解决：** 对必须跨激活实例丢失而存续的工作使用提醒器，恢复提供程序健康状况，减少调度器压力，并使回调具备幂等性。从持久化应用程序状态协调补齐遗漏的业务工作，将回调计数作为辅助信号。

**预防：** 对激活实例作用域内的调度使用定时器，使提醒器工作具备幂等性，监视提供程序健康及调度延迟，并将定时器和提醒器用于尽力而为的计划回调。从持久化状态协调时间关键型工作。<xref:Orleans.Hosting.ReminderOptions.MinimumReminderPeriod> 默认为一分钟；注册操作拒绝低于配置值的周期。配置较小的值会发出生产适用性警告。

<a id="memory-pressure"></a>

## 内存压力

**信号：** 以 MB 表示 GC 可用内存的 `orleans-runtime-available-memory` 较低、进程工作集或 GC 堆增长、GC 暂停时间过长、激活实例频繁变动，或者容器因 OOM 被终止。

**可能原因：** 无界 Grain 状态或缓存、流缓冲区、大型请求有效载荷、遥测基数、激活实例增长、频繁分配内存的代码，或容器限制低于假定的主机容量。

**确认：** 将进程/容器限制、工作集、GC 堆和分配速率、激活实例工作集及流缓存指标关联。在安全可行时于重启前捕获转储或 GC 跟踪，并检查对象保留路径。

**解决：** 降低负载，限制导致问题的数据结构或有效载荷，并在工作可以分区时横向扩容。在提高限制之前识别持续保留的内存增长，确保变更针对内存压力的来源。

**预防：** 设置明确的内存预算，限制缓存/缓冲区/状态，对稳态及恢复进行负载测试，并根据实际容器限制设置告警。遵循[内存压力增大](troubleshooting.md#memory-pressure-rises)中的指导。

<a id="shutdown-exceeds-the-termination-budget"></a>

## 关闭超过终止时间预算

**信号：** 编排器在宽限期后终止进程、生命周期日志在关闭过程中停止，或者成员资格保持 `ShuttingDown`/`Stopping` 状态。

**可能原因：** 平台时间预算短于主机关闭时间；新工作继续到达；生命周期参与者在取消后仍继续运行；Grain/依赖项调用阻塞；或流/提供程序清理超过截止时间。

**确认：** 比较应用程序与平台的截止时间，检查最后的生命周期日志及跟踪记录，并在强制终止前捕获调用栈。确定仍然活动的生命周期阶段或托管服务。

**解决：** 在 Silo 关闭之前停止准入，使阻塞的参与者具备明确上限并响应取消请求，在主机截止时间之后预留平台时间用于进程清理。

**预防：** 在负载和依赖项故障条件下测试关闭，保留滚动发布所需的额外容量，并监视强制终止。遵循[关闭超过时间预算](troubleshooting.md#shutdown-doesnt-complete)中的指导。
