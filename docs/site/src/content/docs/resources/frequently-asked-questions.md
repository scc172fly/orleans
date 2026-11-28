---
title: 常见问题解答
description: 解答 Orleans 应用开发与运维中的常见问题。
ms.date: 08/12/2026
ms.topic: faq
---

<a id="frequently-asked-questions"></a>

# 常见问题解答

按症状诊断时，请先查看[故障排查症状目录](../host/monitoring/troubleshooting-symptom-catalog.md)，再按照[事件处置手册](../host/monitoring/troubleshooting.md)操作。有关面向任务的配置和部署方法，请参阅[操作指南索引](../how-to/index.md)。

<a id="availability-and-support"></a>

## 可用性和支持

<a id="can-i-use-orleans-in-my-project"></a>

### 我可以在自己的项目中使用 Orleans 吗？

可以。Orleans 根据 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)开源，官方包发布在 [NuGet.org](https://www.nuget.org/profiles/Orleans) 上。

<a id="is-orleans-production-ready"></a>

### Orleans 是否已达到生产就绪水平？

是的。Orleans 起源于 Microsoft Research，自 2011 年起即为生产服务提供支持。该项目在 [dotnet/orleans](https://github.com/dotnet/orleans) 上公开开发。

<a id="which-net-target-frameworks-does-orleans-support"></a>

### Orleans 支持哪些 .NET 目标框架？

Orleans 库面向 `net10.0` 和 `net11.0`。

<a id="where-do-i-get-help"></a>

### 在哪里获取帮助？

对于可复现的缺陷和功能提议，请使用 [GitHub Issues](https://github.com/dotnet/orleans/issues)。对于问题咨询和设计讨论，请使用 [GitHub Discussions](https://github.com/dotnet/orleans/discussions) 或 [Discord](https://aka.ms/orleans-discord)。

<a id="hosting"></a>

## 托管

<a id="is-orleans-a-server-product"></a>

### Orleans 是服务器产品吗？

Orleans 是一组用于构建应用的 .NET 库。应用在自己部署和运营的进程中托管一个或多个 Orleans Silo。

<a id="where-can-orleans-run"></a>

### Orleans 可以在哪里运行？

凡是能够运行其受支持 .NET 目标框架的环境，均可运行 Orleans，包括 Linux 和 Windows 主机、容器、Kubernetes、Azure 服务、AWS、其他云、本地部署环境及开发计算机。

<a id="how-many-grains-or-silos-can-a-cluster-contain"></a>

### 一个集群可以包含多少 Grain 或 Silo？

Grain 标识构成虚拟地址空间，Orleans 在可用 Silo 上按需激活 Grain。实际可承载的活跃 Grain 数量和 Silo 数量取决于工作负载及部署环境。

使用类似生产环境的容量测试，确定满足应用性能及恢复目标的集群规模和余量。请参阅[容量规划与扩缩容](../deployment/capacity-planning.md)。

<a id="is-orleans-tied-to-azure"></a>

### Orleans 是否绑定 Azure？

Orleans 可以跨云和托管环境运行。其提供程序生态包括 Azure 服务、关系数据库、DynamoDB、Redis、Cassandra、Consul、ZooKeeper、NATS、SQS 以及自定义提供程序。

<a id="can-browsers-or-mobile-apps-connect-directly-to-silos"></a>

### 浏览器或移动应用可以直接连接 Silo 吗？

应在公共客户端与 Orleans Silo 或网关终结点之间设置经过身份验证的应用协议，例如 HTTPS、SignalR 或其他 API 层。

## Grain

<a id="how-large-should-a-grain-be"></a>

### 一个 Grain 应该多大？

围绕领域实体或一致性边界对 Grain 建模。如果一个键成为吞吐量瓶颈，或拥有过多状态，则 Grain 可能太大；如果一次操作需要在 Grain 之间频繁往返调用，则可能太小。应测量具有代表性的工作负载，而不是依赖固定的状态大小或每秒调用次数规则。

<a id="does-orleans-replicate-grain-state-automatically"></a>

### Orleans 会自动复制 Grain 状态吗？

普通有状态 Grain 通常在集群中只有一个激活实例。易失状态随该激活实例的生命周期存在，并在激活实例停用、迁移或发生故障时丢弃。配置存储提供程序并成功写入 Grain 状态，才能提供持久化恢复能力。应用应自行设计和运营工作负载所需的副本或缓存。

<a id="how-do-i-avoid-hot-grains"></a>

### 如何避免热点 Grain？

普通 Grain 激活实例默认一次处理一个执行轮次，因此即使集群其余部分仍有容量，单个键也可能成为瓶颈。应跨键划分工作、使用分级或层次化聚合、批量执行调用，或为合适的无状态操作使用无状态工作者。将激活实例迁移到负载较低的主机，或迁移到更靠近其调用对象的位置，可以通过减少争用、RPC 开销和延迟提高吞吐量。对键进行分区则可增加并发度。

例如，如果许多 Grain 定期上报计数器或统计信息，可以对各上报者的稳定键进行哈希，将其分配到数量受控的一组中间聚合 Grain。各中间 Grain 合并更新并定期将部分结果发送给最终聚合 Grain。这会分散上报负载并减少中心 Grain 的执行轮次；若某一层仍接收过多汇聚请求，可再增加一层。根据负载测试选择分片数量及上报频率；如果丢失或重复更新会产生影响，还应设计持久化、幂等性或对账机制。

<a id="can-i-choose-where-a-grain-activates"></a>

### 我可以选择 Grain 在哪里激活吗？

可以。Orleans 内置放置策略，并支持自定义放置。除非应用具有经过测量确认的本地性或资源需求，否则应优先采用位置透明性，因为限制性放置会减少运行时在故障和扩缩容时的选择。

<a id="how-do-i-deactivate-a-grain"></a>

### 如何停用 Grain？

通常应让 Orleans 自动停用空闲激活实例。当 Grain 知道自己应在当前执行轮次结束后移除时，可以调用 <xref:Orleans.Grain.DeactivateOnIdle*>。

<a id="failures-and-calls"></a>

## 故障与调用

<a id="what-happens-when-a-silo-fails-during-a-call"></a>

### 调用过程中 Silo 发生故障会怎样？

调用可能失败或超时。集群检测到故障 Silo 后，后续调用可在健康的 Silo 上激活 Grain。调用方仅应在操作可以安全重试时执行有界重试。只有此前已成功写入可用的持久化提供程序，持久状态才可用。

<a id="are-grain-calls-exactly-once"></a>

### Grain 调用具有恰好一次语义吗？

Orleans 默认采用至多一次消息投递。网络故障可能导致调用方无法确定操作是否已执行，因此可重试操作应具有幂等性或携带应用层去重标识。

<a id="what-happens-when-grain-code-runs-too-long"></a>

### Grain 代码运行时间过长会怎样？

Orleans 使用协作式调度。Grain 的执行轮次持续运行直至主动让出执行权或完成，长时间同步工作会延迟该调度器上的其他执行轮次。应保持执行轮次简短，异步等待 I/O，并将大量 CPU 密集型工作移交给适当的执行模型。

<a id="how-do-i-upgrade-an-existing-application"></a>

### 如何升级已有应用？

请遵循[迁移指南](../migration-guide.md)，其中包含各版本的升级历史。概念和教程文档说明受支持的 API，不重复升级历史。

<a id="how-should-i-retry-failed-grain-calls"></a>

### 如何重试失败的 Grain 调用？

当操作具有幂等性或携带应用层去重标识时，可重试暂时性故障。确定性的验证、序列化及并发失败应直接进入相应处理或对账流程。在适当边界使用有限的重试预算以及退避和抖动策略。超时或连接丢失可能导致结果未知，因此在重复执行不安全时应核对业务状态。

<a id="can-reentrancy-fix-a-grain-call-deadlock"></a>

### 可重入性可以解决 Grain 调用死锁吗？

当不可重入的 Grain 等待一个最终又回到该 Grain 的调用时，调用环可能停滞。可重入性能够允许该回调运行，但同时允许交错执行，因此 Grain 的状态不变量必须覆盖这些状态转换。应消除同步阻塞、重新设计可避免的调用环，并仅对已理解并测试交错行为的操作使用 <xref:Orleans.Concurrency.ReentrantAttribute> 或 <xref:Orleans.Concurrency.AlwaysInterleaveAttribute>。请参阅[长时间运行或死锁的 Grain 执行轮次](../host/monitoring/troubleshooting-symptom-catalog.md#long-running-or-deadlocked-grain-turns)。

<a id="operations"></a>

## 运维

<a id="how-many-silos-do-i-need"></a>

### 我需要多少个 Silo？

根据峰值 CPU、内存、连接及依赖项预算、计划应对的故障域损失、发布期间的额外容量，以及代表性负载测试确定 Silo 数量。测量时使用具有代表性的键分布、有效载荷、扇出、存储、流及故障恢复场景。请参阅[容量规划](../deployment/capacity-planning.md)。

<a id="why-didnt-adding-silos-improve-throughput"></a>

### 为什么增加 Silo 后吞吐量没有提升？

瓶颈可能来自热点 Grain、限制性放置、共享存储或流提供程序、网关准入控制、网络带宽或其他依赖项。增加 Silo 会扩展分布式容量，但普通有状态 Grain 对同一键仍只有一个激活实例。进一步扩缩容前，应比较各 Silo 的利用率、激活实例分布、请求拒绝情况、长时间运行的执行轮次和依赖项延迟。

<a id="what-determines-orleans-cost"></a>

### 哪些因素决定 Orleans 的成本？

Orleans 使用 MIT 许可证。部署成本来自计算、内存、网络流量、集群与持久化提供程序、流、遥测以及冗余。Grain 边界会影响成本：频繁的跨 Silo 调用、大型序列化有效载荷、频繁写入状态、大量激活实例以及高基数遥测均可能成为主要成本来源。应测量具有代表性的工作负载，并计入故障域备用容量、发布期间额外容量、备份和可观测性数据保留成本。

<a id="which-grain-storage-provider-should-i-choose"></a>

### 应选择哪种 Grain 存储提供程序？

应综合考虑持久性、一致性和并发行为、延迟、吞吐量及分区限制、区域可用性、备份与还原、安全性、运维熟悉程度和成本。集群成员资格、Grain 存储、提醒器及流可以根据各自的访问模式选择不同后端。使用实际状态大小及键分布验证提供程序的故障和限流行为。

<a id="does-storage-throttling-only-affect-persistence-calls"></a>

### 存储限流只影响持久化调用吗？

存储限流的影响会传导至激活、Grain 执行轮次、调度器、后端及请求延迟。状态读取延迟会推迟激活；写入延迟会占用 Grain 执行轮次；重试消耗调度器和后端容量；由此产生的排队会导致请求超时。应关联分析存储延迟、限流、重试、激活速率和请求延迟。请参阅[存储限流](../host/monitoring/troubleshooting-symptom-catalog.md#storage-throttling)。

<a id="can-one-orleans-cluster-span-multiple-regions"></a>

### 一个 Orleans 集群可以跨多个区域吗？

单个集群要求各 Silo 之间及其与成员资格和目录依赖项之间具有可靠且足够低延迟的连接。广域网会增加延迟和网络分区的概率，并扩大故障域。除非测试证明跨区域延伸的单一集群满足应用的可用性和一致性要求，否则优先使用独立区域集群，并明确应用层路由、复制、所有权及故障转移语义。

<a id="how-do-i-prevent-split-brain"></a>

### 如何防止脑裂？

每个集群使用一个共享且持久化的成员资格提供程序、稳定的集群标识、互相可达的通告终结点、时钟同步，以及能在计划收敛时间内检测、隔离并终止失效 Silo 的基础设施。在疑似网络分区期间，先隔离失效成员，再编辑成员资格记录。需要跨区域存续的应用应定义跨集群的所有权和对账机制。请参阅[成员资格和目录频繁变动](../host/monitoring/troubleshooting-symptom-catalog.md#membership-and-directory-churn)。

<a id="can-membership-churn-create-duplicate-grain-activations"></a>

### 成员资格频繁变动会产生重复的 Grain 激活实例吗？

故障检测和目录收敛期间可能出现短暂的重复激活实例。运行时解决目录冲突，而应用需要为外部副作用和自定义目录实现提供安全语义。Grain 存储并发令牌可检测冲突的存储写入，并报告提供程序冲突；应用应为外部副作用提供幂等或去重机制。反复出现的重复激活应作为成员资格、网络或目录健康问题的症状进行调查。

<a id="whats-the-difference-between-a-grain-timer-and-a-reminder"></a>

### Grain 定时器与提醒器有什么区别？

定时器提供激活实例范围内的调度，并随该激活实例结束。提醒器通过配置的提醒器提供程序存储调度计划，并可在 Silo 或激活实例重启后重新激活 Grain。两者均以尽力而为的方式提供计划回调，应用工作应能容忍延迟和重复。请参阅[提醒器与定时器时序](../host/monitoring/troubleshooting-symptom-catalog.md#reminder-and-timer-timing)。

<a id="why-did-reminders-or-timers-run-late"></a>

### 为什么提醒器或定时器延迟执行？

调度器压力、CPU 或 GC 暂停、回调执行时间超过其周期、Silo 重启、成员资格变化以及提醒器提供程序延迟都可能导致回调推迟。应确定正在使用的机制，比较回调持续时间与周期，并将时序与运行时及提供程序健康状况关联分析。使用持久化应用状态对账处理时间敏感的业务工作。

<a id="how-do-i-make-rolling-upgrades-safe"></a>

### 如何确保滚动升级安全？

确保相邻版本的 Grain 接口、序列化器、存储状态、队列或流中的有效载荷、提供程序架构及配置双向兼容。保留稳定的序列化成员 ID 和别名，限制发布并发度，维持额外容量，并在部署前定义回滚方案。测试从旧版到新版以及从新版到旧版的调用，并验证已有数据的读取。请参阅[优雅关闭与升级](../deployment/upgrades.md)。

<a id="why-did-serialization-start-failing-after-deployment"></a>

### 为什么部署后序列化开始失败？

混合版本可能在类型契约、序列化器注册、成员 ID、别名或包版本上存在分歧，也可能是新版本无法读取此前持久化或排队的旧数据。保留发生故障的类型和有效载荷来源，停止发布，并在迁移数据前使用兼容的读取器或执行回滚。请参阅[版本变更后的序列化失败](../host/monitoring/troubleshooting-symptom-catalog.md#serialization-failures-after-a-version-change)。

<a id="what-should-i-collect-before-restarting-an-unhealthy-silo"></a>

### 重启不健康的 Silo 前应该收集哪些信息？

收集 UTC 时间戳、部署与配置变更、相关客户端和 Silo 的日志、跟踪、指标、成员资格视图、通告终结点、提供程序健康状况及平台事件。对于调度器、CPU 或内存问题，在安全的情况下捕获范围受限的进程跟踪或转储。对机密和 Grain 状态进行脱敏。首先查阅 [Orleans 症状与信号目录](../host/monitoring/troubleshooting-symptom-catalog.md)。
