---
title: "Orleans 症状和信号目录"
description: "找出 Orleans 生产补救措施,按例外、日志事件、度量或观察到的行为进行查找。"
ms.date: 08/11/2026
ms.topic: troubleshooting
---

<a id="orleans-symptom-and-signal-catalog"></a>

# Orleans 症状和信号目录

如果您有特定的例外、事件编号、度量或观察到的行为, 请使用此目录。 搜索此页面以获取字面信号, 然后使用链接的运行簿来进行全部调查序列 。

例外或事件会指出 Orleans 注意到问题所在。 保存例外链、结构化的 <xref:Microsoft.Extensions.Logging.EventId>、类别、跟踪身份、 silo 身份、分组成员资格观点和第一次发生的情况,然后将证据与确定根源相关联。

<a id="quick-index"></a>

## 快速指数

| 信号信号 | 开始于 |
|---|---|
| <xref:Orleans.Runtime.Messaging.ConnectionFailedException>,无连接网关 | [连接和网关失败](#connection-and-gateway-failures) |
| <xref:Orleans.Runtime.SiloUnavailableException>, <xref:Orleans.Runtime.OrleansMessageRejectionException> | [Silo 无法或信件被拒绝](#silo-unavailable-or-message-rejected) |
| <xref:Orleans.Runtime.GatewayTooBusyException>,拒绝或加载断层电文 | [网关或集集超载](#gateway-or-cluster-overload) |
| <xref:System.TimeoutException>, `orleans-app-requests-timedout` | [请求超时](#request-timeouts) |
| `orleans-scheduler-long-running-turns`,呼叫一个 grain 停止前进 | [长途或陷入僵局 grain](#long-running-or-deadlocked-grain-turns) |
| <xref:Orleans.Storage.InconsistentStateException>,ETag或版本冲突 | [存储一致性失灵](#storage-consistency-failures) |
| 存储错误或延迟度随着后端节流而上升 | [储存节压](#storage-throttling) |
| 部署后出现序列或编码器例外 | [版本更改后发生串级失败](#serialization-failures-after-a-version-change) |
| 重复的成员变化、错过的短线、重复激活 | [成员和目录](#membership-and-directory-churn) |
| 1个 grain 进展缓慢,而分组能力仍保留 | [热 grains 和偏斜安置](#hot-grains-and-skewed-placement) |
| 提醒或计时器会迟到、两次或错过预期运行时间 | [提醒和计时时间](#reminder-and-timer-timing) |
| 可用内存跌落、GC暂停上升或容器被OOM杀死 | [内存压力](#memory-pressure) |
| 超时超过终止的宽限期 | [超时超过终止预算](#shutdown-exceeds-the-termination-budget) |

<a id="connection-and-gateway-failures"></a>

## 连接和网关失败

**信号:** <xref:Orleans.Runtime.Messaging.ConnectionFailedException>,多次连接尝试, `orleans-client-connected-gateways` 为0,或客户准备状态仍然是假的。

**原因可能是:** 客户和 silo 组特性不同;网关供应商返回一个空的主动 -- --silo 集;广告网关地址解决客户路由以外的问题;DNS、防火墙、NAT或网络政策块交通;或TLS名称、信任或客户证书要求不同。

**确认:** 查询客户网络的网关源,比较集群和服务标识,测试每个广告网关的DNS和TCP连接。对于TLS,保留内部握手例外并检查证书 SAN、信任链、EKU和 `TargetHost`。

**补救:** 正确的身份/提供者配置或公布客户可前往的地址。替换无效证书或信任配置。地表确定性配置故障立即发生,并绑定启动重试。

**预防:** 在每个部署网络上准确公布的确切终端点验证,并在客户有一个可用的网关后报告准备情况。 [客户连接失败](troubleshooting.md#client-cant-connect).

<a id="silo-unavailable-or-message-rejected"></a>

## Silo 无法或信件被拒绝

**信号:** <xref:Orleans.Runtime.SiloUnavailableException>、 <xref:Orleans.Runtime.OrleansMessageRejectionException>、连接关闭或故障围绕成员变动。

**原因:** 一个 silo 已停止或失去网络可达性;成员趋同正在进行;在车轮期间使用了一个破碎激活地址;目的地关闭;超载导致拒绝。

**确认:** 例外时间戳和目标 silo 与成员资格、部署、套接、平时和处理事件相关。确定呼叫是否通过使用跟踪或应用程序操作ID达到应用代码。

**补救:** 恢复集群/网络健康或完成推出。 grain 参考对以后的调用仍然有效。只重试受约束、无能力或重复操作,因为调用失败的结果未知。

**预防:** 启动期间保存失灵主机容量,使可重新测试的操作无能,并测试负载中的会籍损失。 [成员变更时电话不能接](../../deployment/troubleshooting-deployments.md#calls-fail-during-membership-changes).

<a id="gateway-or-cluster-overload"></a>

## 网关或集集超载

**信号:** <xref:Orleans.Runtime.GatewayTooBusyException>, `Shedding load` 拒绝、上升 `orleans-messaging-rejected` 或 `orleans-gateway-load-shedding`、增加排队时的延迟期或吞吐量下降而需求增加。

**原因:** 网关卸载延迟启用,CPU或内存超过其配置的阈值; grain 排程器饱和; grain 热;线性池或CPU断绝;或储存/流依赖性低于提供的比率。

**确认:** 将反向、完成、拒绝和超时率与CPU、线状组合队列、长转、激活计数和依赖性延缓度进行比较。比较 silos:一个外端表示偏斜或主机问题;整个集群的转换表明共有能力或依赖力压力。

**补救措施:** 首先减少入场和任选工作。在安全的重试边界使用带紧凑的指数反弹。只有当工作能够分割和共享依赖时才能缩放大小。

**预防:** 测试真实密钥和扇门退出分布,在拒绝开始之前提醒,并维持一个网关和依赖关系的能力信封。 [集组超载](troubleshooting.md#the-cluster-is-overloaded).

<a id="request-timeouts"></a>

## 请求超时

**信号:** <xref:System.TimeoutException>,增加 `orleans-app-requests-timedout` (被 `grain_type`标记),或从 `orleans-app-requests-latency` 直方图中不断上升的桶数或百分位数。

**原因:** 在行刑前排队,长途 grain 转弯,循环呼叫,阻断工作,储存缓慢或有其他依赖关系,成员身份恢复,或网络丢失。

**确认:** 遵循抽样追踪,以确定该呼叫是否达到 silo ,以及等待的时间。用长转、拒绝/发送的信息、CPU、GC、线性集合、会籍和依赖性延缓来修改超时。

**补救:** 去除瓶颈或减少负荷。当测量的有效延迟超过预算时,增加超时时间。将超时操作视为结果未知并调和应用状态。

**预防:** 设置端对端的延时预算、有约束的依赖电话和队列,在安全的地方宣传取消,使用应急或重复来进行回调。 [Grain 呼叫暂停](troubleshooting.md#grain-calls-time-out).

<a id="long-running-or-deadlocked-grain-turns"></a>

## 长途或陷入僵局 grain

**信号:** `orleans-scheduler-long-running-turns` 增加;呼叫一个激活停止进展;跟踪显示应用执行期长;或堆叠显示 `.Result`、 `.Wait()`、监测争议,或循环 grain 呼叫。

**原因可能是:** 同步阻塞、CPU重力工作、无环循环、外部I/O,且在请求预算之外有未受约束的等待或延迟时间,或者跨grain 呼叫周期。再生可以推进一个周期,而同步屏蔽则继续占据调度器。

**确认:** 抓取问题生效时的追踪或倾弃。 确定 grain 类型和方法, 检查管理过的堆叠和排程队列, 绘制呼叫图。 请检查是否正在等待一个最终回拨的电话, 未恢复激活 。

**补救:** 清除阻塞等候;使用捆绑的无同步操作;断开呼叫周期;将CPU-重力工作从 grain 转机移出;或重新设计协议。对于必须重新进入启动者的已知呼叫路径,请在链上设置 <xref:Orleans.Runtime.RequestContext.AllowCallChainReentrancy*> 。只有在证明断裂状态过渡是安全的之后才应用 <xref:Orleans.Concurrency.ReentrantAttribute> 或 <xref:Orleans.Concurrency.AlwaysInterleaveAttribute> 。见 [呼叫链回转](../../grains/request-scheduling.md#call-chain-reentrancy)。

**预防:** 保持交界,审查grain 调用图形、测试回回路和文档 grain 交接下的各种变量。 [Grain 转弯似乎卡住了](troubleshooting.md#grain-turns-appear-stuck).

<a id="storage-consistency-failures"></a>

## 存储一致性失灵

**信号:** <xref:Orleans.Storage.InconsistentStateException>,ETag/version/先决条件冲突,或一再未写 grain 状态。

**原因:** 同时撰写者、在加入或按惯例指令失效期间重复激活、应用代码重复使用废旧状态,或更换同一记录的带外作者。

**确认:** 保存存储和预期版本值、 grain 类型/钥匙、供应商操作和成员时间。确定是否写入了记录,确定是一个以上的激活程序还是外部程序。日志标识符和版本,同时保持 grain 状态和保密被编辑。

**补救:** 停止不安全作家,并根据应用语义对记录进行调和。 突如其来冲突并保存新记录。 如果一个 <xref:Orleans.Storage.InconsistentStateException> 摆脱了启动该程序而使其产生, 则激活后再拨的 Orleans 禁用可以重新装入状态。

**预防:** 每个州保持一名权威作家,使用供应商的货币质证、通过该作者更新路线以及从模糊的写作和激活中测试回收。

<a id="storage-throttling"></a>

## 储存节压

**信号:** `orleans-storage-read-errors`、 `orleans-storage-write-errors`或 `orleans-storage-clear-errors` 上升;相应的 `orleans-storage-*-latency` 工具长度;供应商例外报告有递减或费率限制;请求的超时之后。

**原因:** 提供的排放量过低;分区键被斜斜;连接池耗竭;回溯放大负载;或激活/回收波产生爆裂。

**确认:** 相关供应商请求单位、分区节流、延迟和重试数量,与 Orleans 存储操作和激活计数进行。通过经批准的低心胸分隔分类比较受影响的 grain 键,并保持原 grain 键的不进入度量。

**补救:** 减少入场和重新测试放大、增加或重新分配后端能力,并解除热分区。仅使用供应商有记录的短暂故障政策,并附带约束性重试预算。

**预防:** 能力测试储存,其实际状态大小和关键分布、预算重整、在将电波扩散到呼叫超时前的延迟警报以及错开恢复工作。

<a id="serialization-failures-after-a-version-change"></a>

## 版本更改后发生串级失败

**信号:** <xref:Orleans.Serialization.CodecNotFoundException> 报告 `Could not find a codec for type ...` 或 `Could not find a copier for type ...`;另一个序列化例外报告无支持的电线类型或未知/需要的类型或字段; `KeyNotFoundException` 报告 `Could not find a base type serializer for type ...` 或 `Could not find a value serializer for type ...`;或启动/储存在部署后开始失效。

**原因:** 混合版本使用不兼容的合同或序列号身份;可连序类型或成员身份发生变化;成员身份证被重新使用或重新编号;改名类型缺乏稳定的别名;所要求的序列号登记不同;或持续/排队数据含有旧的表示法。

**确认:** 捕捉全部例外和类型名称,确定有效载荷是否来自呼叫、存储、流、提醒或队列,比较旧/新生成的序列元数据和软件包版本。 在改变生产数据之前,先阅读有代表性的旧数据和新版本,再复制旧数据和新版本。

**补救措施:** 滚回一个兼容版本,或部署一个理解两种表述的读者,然后故意迁移数据。在确定回收和保留要求之前,保留无法读取的状态和队列。

**预防:** 保持稳定 `[Id(n)]` 值和别名,用两个版本方向测试滚动升级,并在兼容性测试中包括耐久和排队的有效载荷。见 [部署和回缩](../../migration/deployment-and-rollback.md)。

<a id="membership-and-directory-churn"></a>

## 成员和目录

**信号:** silos 反复离开 `Active`、错过 pings、成员警告、短暂重复激活、重复地址失效或电话在新旧启动地址之间回响。

**原因:** 进程或网络暂停、CPU/GC饥饿、会员-存储时间或抽动、广告地址不稳定、时钟问题、积极推出、网络分割或习惯 grain- 指令一致性/清理行为。

**确认:** 比较来自所有会员的会籍观点和时间戳 silos 平台活动、提供商的延时时间、 pings 和广告广告的终点。 对于怀疑分裂大脑的人,确定是否孤立 silos 需要确认会员、供应商、网络和平台证据,才能诊断分割。

**补救措施:** 停止推出/大规模加入,维护更大的健康分割区,恢复会员储存和网络健康,并允许趋同。在改变会籍记录之前隔离一个固定的分隔区:一个运行中的 silo ,读自会籍条目,当 `Dead` 结束时,它就会自动终止。对于海关目录,请遵循其文件记录的修理程序。

**预防:** 使用一个共同的、持久的成员提供机构;保持终点稳定,相互可达;维持时钟同步和故障域能力;测试隔断和滚动替换。 [成员不稳定](troubleshooting.md#membership-is-unstable) 和 [灾后恢复](../../deployment/disaster-recovery.md).

<a id="hot-grains-and-skewed-placement"></a>

## 热 grains 和偏斜安置

**信号:** 1 grain 型号/钥匙的排队或延迟度高,而其他 silos 则有备用能力;小一套 silos 的脉冲或激活不相称;或者由于添加了 silos ,吞吐量保持平。

**原因:** 一个一致性边界得到的交通量最多,目标为“扇门进入”,钥匙分布不均,受资源限制的职位太少,资格 silos,或职位安排最优化地点,但集中工作。

**确认:** 比较受约束的 grain 类型/操作尺寸的延缓和吞吐量,检查有代表性的痕迹和仪表板方法特征分析,并安全地分析应用软件的关键分布。区别热活化与一般昂贵的 grain 类型。

**补救措施:** 将域内工作量分成关键部分,使用等级汇总或批量,利用无国籍工人从事适当的无国籍工作,或者放松不必要的限制性安置规则。 普通状态激活在移动时保留单向计算法;键分割增加平行性。

**预防:** 负载测试偏斜和名人键的流量,界定按键限制,通过经核准的低心肌功能分类监测放置/激活分布,并保持原始 grain 键私密。见 [我该如何避免热 grains?](../../resources/frequently-asked-questions.md#how-do-i-avoid-hot-grains)。

<a id="reminder-and-timer-timing"></a>

## 提醒和计时时间

**信号:** `orleans-reminders-tardiness` 上升, `orleans-reminders-ticks-delivered` 摊位,回调延迟,暂停后回调,提醒似乎不止一次,或停止/重新启动后回拨停止。

**原因可能是:** 计时回调预期会在启动损失中幸存;无法提供提醒或依赖成员的情况;调度器/CPU压力延迟执行;回调期限超过时限;或者应用代码假定一交货就完全交货。

**确认:** 首先确定应用程序是使用一个 grain 计时器还是提醒。 与激活寿命、 silo 重新启动、排程延迟、成员身份、提醒提供者健康以及回调时间相匹配的回溯时间戳。 使用耐用应用程序操作代号来检测重复的工作 。

**补救措施:** 对必须幸存的启动损失、恢复供应商健康、减少调度压力和调回无能的工作使用提醒。 重新协调从耐用应用状态中错失的商业工作,并使用回拨计数作为支持信号。

**预防:** 选择启动范围列表的计时器,让提醒工作成为一等强者,监测供应商健康和时间安排延误,使用计时器和提醒进行最佳工作排回。调和耐用状态的时间临界值工作。 <xref:Orleans.Hosting.ReminderOptions.MinimumReminderPeriod> 默认为一分钟;登记拒绝低于配置值的时期。配置低值后发出生产可操作性警告。

<a id="memory-pressure"></a>

## 内存压力

**信号:在甲基溴中,** 低 `orleans-runtime-available-memory` ,含有GC可用内存、不断增长的工艺或GC堆积、GC暂停、激活或容器OOM杀伤。

**原因可能是:** 未受约束的 grain 状态或缓冲处、流缓冲处、大要求的有效载荷、遥测基点、启动增长、分配重代码或低于假定东道国容量的集装箱限值。

**确认:** 相关流程/集装箱限制、工作设定、GC堆积和分配率、启动工作设置和流缓存量。在安全并检查保留路径时,在重新启动之前先抓取垃圾场或GC追踪。

**补救措施:** 减少负荷,约束负责的数据结构或有效载荷,并在工作分区时缩放。在提高限制之前先确定保留的增长,以便改变能够解决内存压力的来源。

**预防:** 设定明确的存储预算、捆绑缓存/弹壳/状态、负载测试稳定状态和回收状况,并针对实际集装箱限值保持警惕。 [内存压力上升](troubleshooting.md#memory-pressure-rises).

<a id="shutdown-exceeds-the-termination-budget"></a>

## 超时超过终止预算

**信号:** 管弦乐队在宽限期后将过程杀死,生命周期记录在停产期间停止,或成员资格仍然 `ShuttingDown`/`Stopping`。

**原因:** 平台预算比主机关闭短;新的工作继续到来;生命周期参与者继续过去取消; grain/依赖电话块;或流/提供者清理超过最后期限。

**确认:** 比较应用和平台最后期限,检查最后生命周期日志和跟踪,在强制终止前采集堆积。

**补救措施:** 在 silo 关闭前停止入境,使被封持的参与者了解受封阻和取消,并在清理过程的东道国最后期限之后离开平台的时间。

**预防:** 负载和扶养能力失灵时,测试关闭,保留推出增援能力,监测强制终止。 [超时超过预算](troubleshooting.md#shutdown-doesnt-complete).
