---
title: "经常问到的问题"
description: "对有关开发和运行 Orleans 应用程序的共同问题的答复。"
ms.date: 08/12/2026
ms.topic: faq
---

<a id="frequently-asked-questions"></a>

# 经常问到的问题

对于基于症状的诊断,从 [解决症状目录中的麻烦](../host/monitoring/troubleshooting-symptom-catalog.md)开始,然后依 [事件运行手册](../host/monitoring/troubleshooting.md)。对于面向任务的设置和部署方法,见 [如何指导索引](../how-to/index.md)。

<a id="availability-and-support"></a>

## 可用性和支助

<a id="can-i-use-orleans-in-my-project"></a>

### 借我用用用 Orleans 在我的项目?

是。 Orleans 是 [麻省理工学院许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)下的开放源,正式的软件包在 [NuGet.org](https://www.nuget.org/profiles/Orleans)上公布。

<a id="is-orleans-production-ready"></a>

### 是 Orleans 准备生产了吗?

是。 Orleans 始于 Microsoft 研究,自2011年起开始提供生产服务动力,该项目在 [dotnet/orleans](https://github.com/dotnet/orleans)开放时开发。

<a id="which-net-target-frameworks-does-orleans-support"></a>

### 哪些 .NET 目标框架支持 Orleans ?

Orleans 图书馆目标 `net10.0` 和 `net11.0`。

<a id="where-do-i-get-help"></a>

### 我哪里有帮助?

使用 [GitHub 问题](https://github.com/dotnet/orleans/issues) 来复制错误和功能建议。 使用 [GitHub 讨论](https://github.com/dotnet/orleans/discussions) 或 [不一致](https://aka.ms/orleans-discord) 来提问和设计对话。

<a id="hosting"></a>

## 东道主

<a id="is-orleans-a-server-product"></a>

### Orleans 服务器产品吗?

Orleans 是一套用于构建应用程序的 .NET 图书馆。一个应用程序在其部署和运行过程中,一个或多个程序主机是 Orleans silos 。

<a id="where-can-orleans-run"></a>

### Orleans 跑到哪里去?

Orleans 运行在其所支持的 .NET 目标运行的任何地点,包括Linux和Windows主机、集装箱、 Kubernetes服务、 Azure 服务、AWS、其他云层、房地环境和开发机。

<a id="how-many-grains-or-silos-can-a-cluster-contain"></a>

### 有多少 grains 或 silos 集群能包含吗?

Grain 指定一个虚拟地址空间, Orleans 激活 grains 现有各种需求 silos实际积极grain 和 silo 视工作量和部署环境而定。

使用类似生产能力测试来确定应用软件的性能和恢复目标的适当组群大小和顶部。 [能力规划和规模](../deployment/capacity-planning.md).

<a id="is-orleans-tied-to-azure"></a>

### 是 Orleans 绑结 Azure?

Orleans 其提供方生态系统包括 Azure 关系数据库, DynamoDB, Redis卡珊德拉 领事 ZooKeeper、NATS、SQS和海关供应商。

<a id="can-browsers-or-mobile-apps-connect-directly-to-silos"></a>

### 浏览器或移动应用程序能否直接连接到 silos?

放置一个经认证的申请协议, 如 HTTPS, SignalR,或者 API 公共客户和 Orleans silo 或网关端点。

## Grains

<a id="how-large-should-a-grain-be"></a>

### grain 的大小应该有多大?

模型 a grain 围绕一个域实体或一致性边界周围。 grain 如果一个密钥成为吞吐瓶颈或拥有过度状态,那么它可能太大。 如果一个操作需要多次交谈, 则该键会变得太小。 grains衡量代表的工作量,而不是依赖固定的州规模或每秒呼叫规则。

<a id="does-orleans-replicate-grain-state-automatically"></a>

### Orleans 自动复制 grain 状态吗?

普通状态 grain 通常在集束中有一个激活。 挥发性状态继而延续启动的寿命, 当激活停止、 迁移或失败时被丢弃。 一个配置好的存储提供者和成功的 grain 写道提供了耐久恢复。 应用程序设计和操作其工作量所需的复制品或缓存。

<a id="how-do-i-avoid-hot-grains"></a>

### 我该如何避免热 grains?

普通的 grain 激活程序默认时一次转,因此即使组群其余部分有能力,单个键也可能成为瓶颈。 隔开键盘、 使用分阶段或分级集聚、 批次呼叫或者利用无国籍工人进行合适的无国籍行动。 将启动转移到一个载量较少的主机或者更接近 grains 的地方可以减少争议、 RPC 间接费用和延缓度,从而改善吞吐量。 分割键会增加通货性 。

例如,如果许多 grains 定期汇报反差或统计数据,则每个报告人就将稳定的钥匙放在一组受控制的中间聚合器 grains。每个中间 grain 合并更新并定期向最后汇总器发送部分结果。这分配了报告负荷,减少了中央 grain的转弯次数;如果某个阶段仍然收到太多的粉丝,又增加一个级别。选择从负载测试中进行硬计和报告音量,设计耐久性、不合格性,或者在丢失或重复更新时进行调节很重要。

<a id="can-i-choose-where-a-grain-activates"></a>

### 我能选择一个 grain 激活器在哪里吗?

是。 Orleans 包括安置策略和支持自定安置。除非申请有衡量地点或资源要求,否则最好具有地点透明度,因为限制性安置可以减少在失败和规模扩大时的运行时间选择。

<a id="how-do-i-deactivate-a-grain"></a>

### 我该如何解除一个 grain?

通常, 请让 Orleans 停止闲置激活。 当一个 grain 知道它应该在当前转弯后被删除时, 它可以调用 <xref:Orleans.Grain.DeactivateOnIdle*>。

<a id="failures-and-calls"></a>

## 失败和呼号

<a id="what-happens-when-a-silo-fails-during-a-call"></a>

### 当一个 silo 在通话中失败时会发生什么?

调用失败或超时。 在集检测到失败后 silo,稍后的呼叫可以激活 grain 健康日的月经 silo。只有在操作安全可重试时才应当使用约束性回调。只有向可用的耐用提供者成功写下,才能使用持久性状态。

<a id="are-grain-calls-exactly-once"></a>

### 是否为 grain 确切的通话一次?

Orleans 使用默认情况下最接近于一次发送信件的邮件。 网络故障可能会让呼叫者不确定操作是否运行, 所以可重新测试的操作应该是无能的或带有应用程序级重复身份 。

<a id="what-happens-when-grain-code-runs-too-long"></a>

### 当 grain 代码运行过长时会怎样?

Orleans 使用合作排期。一个 grain 转弯,直到它产生或完成,而长期同步的工作会推迟该调度器的其他转弯。保持短跑,等待 I/O,并将大量的CPU定式工作移到适当的执行模式。

<a id="how-do-i-upgrade-an-existing-application"></a>

### 如何提升现有应用程序?

跟随包含版本特定升级历史的 [移徙指南](../migration-guide.md)。概念和教义文件描述支持的API, 而没有重复更新历史 。

<a id="how-should-i-retry-failed-grain-calls"></a>

### 我该如何重试失败 grain 打电话?

当操作为一无所有或带有应用程序级重复身份时, 重试瞬态故障。 将确定性验证、 序列化和同值货币错误直接发送到处理或对账路径上。 在适当的边界使用带回落和抖动的捆绑式重算预算。 超时或连接损失可能会让结果未知, 所以当重复执行不安全时会调和商业状态 。

<a id="can-reentrancy-fix-a-grain-call-deadlock"></a>

### 修复 a grain 要求陷入僵局?

当一个非复发 grain 等待最终返回该 grain的呼叫时,呼叫周期会暂停。再回调可以让回调运行并允许中断,因此 grain的状态变量必须覆盖这些过渡。删除同步阻塞、重新设计可避免循环,并在其内插功能被理解和测试的操作中使用 <xref:Orleans.Concurrency.ReentrantAttribute> 或 <xref:Orleans.Concurrency.AlwaysInterleaveAttribute> 。见 [长途或陷入僵局 grain](../host/monitoring/troubleshooting-symptom-catalog.md#long-running-or-deadlocked-grain-turns)。

<a id="operations"></a>

## 业务业务业务

<a id="how-many-silos-do-i-need"></a>

### 我需要多少个 silos ?

生成来自顶点CPU、内存、连接和依赖预算的 silo 计数,计划失败-域损失,推出式激增和具有代表性的负载测试。用有代表性的钥匙分布、有效载荷、扇扇出、存储、流和故障回收等计量。见 [能力规划](../deployment/capacity-planning.md)。

<a id="why-didnt-adding-silos-improve-throughput"></a>

### 为什么没有增加 silos 改进输送量?

瓶颈可能是热 grain、限制性安置、共享存储或流源提供者、网关进入、网络带宽或其他依赖性。添加 silos 扩大了分布容量,而普通状态的 grain 保留了一个键的激活功能。在进一步扩展之前,比较每个silo 的使用、启动分配、拒绝、长线旋转和依赖性延缓等。

<a id="what-determines-orleans-cost"></a>

### 由什么决定 Orleans 成本?

Orleans 部署费用来自计算、记忆、网络交通、集群和持久性提供者、流、遥测和冗余。 Grain 影响成本的界限:silo 高心电感测可以控制着电话、大型连续有效载荷、频繁的州写作、高活化计数和高心智力遥测。 衡量具有代表性的工作量,包括故障余能、推出式激增、备份和可观察性保留。

<a id="which-grain-storage-provider-should-i-choose"></a>

### 我该选择哪个 grain 存储供应商?

从耐久性、一致性/通货行为、延时、吞吐量和分割限值、区域可用性、备份/存储、安全性、操作熟悉性和成本中选择。组合、 grain 存储、提醒和流可以使用根据其访问模式选择的单独的后端。验证提供者的失败以及以您状态大小和关键分布进行抽动的行为 。

<a id="does-storage-throttling-only-affect-persistence-calls"></a>

### 储存节流是否只影响持久性呼叫?

存储节流器会扩散到激活、 grain- 转、 排程器、 后端和请求延时。 延迟状态读取延迟激活; 延迟写作会保持 grain 转转; 重新输入消耗调度器和后端输出能力; 并由此导致的队列会生成请求超时。 Correlate 封存拉长、 节流、 重排、 启动速度以及请求延时 。 见 [储存节压](../host/monitoring/troubleshooting-symptom-catalog.md#storage-throttling)。

<a id="can-one-orleans-cluster-span-multiple-regions"></a>

### 一个 Orleans 组群能否跨越多个区域?

单一的集群要求 silos 之间及其成员和目录依赖者之间可靠、足够低的长距离连接。广域网增加了延迟和分割概率,并扩大了故障领域。 更可取的是具有明确应用级别路径、复制、所有权和语义不全的独立区域集群,除非测试证明一个扩展的集群符合应用程序的可用性和一致性要求。

<a id="how-do-i-prevent-split-brain"></a>

### 我如何防止脑分裂?

使用每个分组的一个共享、持久成员提供方、稳定的集群身份、相互可达到的广告终端点、时钟同步以及能够在计划合并间隔内检测、隔离和终止破碎 silos 的基础设施。在可疑的分割期间,在编辑成员资格记录之前将老化成员孤立起来。需要跨区域生存的应用程序应界定所有制和跨组间对账。见 [成员和目录](../host/monitoring/troubleshooting-symptom-catalog.md#membership-and-directory-churn)。

<a id="can-membership-churn-create-duplicate-grain-activations"></a>

### 成员是否可创建重复的 grain 启动?

在检测失败和目录趋同期间,可发生短暂的重复激活。运行时间解决目录冲突,而应用程序则为外部副作用和自定义目录实施提供安全的语义。 Grain 存储同货币符号可以探测相互矛盾的储存写作和表面提供者冲突;应用程序提供对外部副作用的不便或减损。调查作为成员、网络或目录健康症状的重复重复。

<a id="whats-the-difference-between-a-grain-timer-and-a-reminder"></a>

### 和两个 grain 以计时和记念,

计时器提供激活范围范围的时间安排和结束启动该激活的结束。提醒通知通过配置的提醒提供者存储其时间表,并可以重新启动 grain a 之后 silo 或重新启动。 两者都提供最佳努力的预定回调, 应用程序工作应该容忍延迟和重复。 见 [提醒和计时时间](../host/monitoring/troubleshooting-symptom-catalog.md#reminder-and-timer-timing).

<a id="why-did-reminders-or-timers-run-late"></a>

### 为什么提醒或计时器迟到?

调度器压力、 CPU 或 GC 暂停, 需要超过其时间的回调, 重新启动 silo 、会籍变更和提醒提供者的延时性等, 都可以全部延迟回溯。 决定使用哪种机制, 将回溯期与周期进行比较, 以及时间与运行时间和提供者健康相关。 使用耐久应用状态来协调对时间敏感的商务工作 。

<a id="how-do-i-make-rolling-upgrades-safe"></a>

### 我怎样才能安全地升级滚动升级?

保持相邻版本在 grain 接口、 序列器、 存储状态、 排队/ 流载荷、 供应商系统图和配置的双向兼容性。 保存稳定的连续式成员身份和别名, 限制推出货币, 维持快速增速能力, 并在部署前定义回放 。 测试旧电话和新电话, 并读出现有数据 。 见 [优优优停机和升级](../deployment/upgrades.md)。

<a id="why-did-serialization-start-failing-after-deployment"></a>

### 为什么连号化在部署后开始失效?

混合版本可能会对类型合同、序列器注册、成员识别、别名或包件版本产生分歧,或者新版本可能无法读取持续数据或排队的旧数据。 保存失效的类型和有效载荷源,停止推出,在迁移数据前使用兼容的阅读器或回放。 见 [版本更改后发生串级失败](../host/monitoring/troubleshooting-symptom-catalog.md#serialization-failures-after-a-version-change)。

<a id="what-should-i-collect-before-restarting-an-unhealthy-silo"></a>

### 重开不健康的 silo前,我该收集什么?

抓取统一时戳、部署和配置变化、 来自相关客户的日志和 silos、 跟踪、 度量、 会员观点、 广告告示端点、 提供者健康和平台事件。 对于调度器、 CPU 或记忆问题, 请在安全的情况下捕获连接的进程追踪或丢弃。 重命名机密和 grain 状态。 从 [Orleans 症状和信号目录](../host/monitoring/troubleshooting-symptom-catalog.md)开始 。
