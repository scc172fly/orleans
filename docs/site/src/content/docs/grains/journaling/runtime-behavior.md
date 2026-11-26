---
title: "记录运行时行为和一致性"
description: "理解 Orleans 日记激活、写、恢复、压缩、计算和故障语义。"
ms.date: 10/19/2026
ms.topic: conceptual
---

<a id="journaling-runtime-behavior-and-consistency"></a>

# 记录运行时行为和一致性

Orleans 日刊分配 <xref:Orleans.Durable.Journaling.JournalId> 对应对 grain 国家经理将该日记的回收和储存工作连成系列。

<a id="activation-and-recovery"></a>

## 激活和恢复

标准 grain- 范围 <xref:Orleans.Durable.Journaling.IDurableStateManager> 登记在册的 <xref:Orleans.Durable.Journaling.JournalingHostingExtensions.AddJournaling*> 注册入学 grain 与激活装置一起建造的寿命周期 <xref:Orleans.Runtime.IGrainContext>。解决一个关键永久状态也解决了这个经理人。每个启动的经理人在其注入的州和特性中共享一个经理人。它的注册处拥有国家创建和约束,因此通过 <xref:Orleans.Durable.Journaling.IDurableStateManager.GetOrAddState*>,它输入的扩展助手或键入注射也返回了同样的情景。

构造体注入登记在生命周期启动前的耐久状态。 Orleans 完成 grain 构建器并指定 <xref:Orleans.Runtime.IGrainContext.GrainInstance>,然后运行任何共享的激活装置,然后调用 grain 对象的 `Participate` 方法并开始生命周期。一个功能可以首先解决管理者的问题,宣布额外的激活范围状态,并注册其参与同步动作的人。所有订阅都建立于生命周期启动之前。

标准管理员一次性参加,支持普通 <xref:Orleans.Grain> 子类、应用程序所属基地、 <xref:Orleans.IGrainBase> 实施和 <xref:Orleans.Durable.Journaling.DurableGrain> 便利基础。其 grain-受约束的建造商通过现有的生命周期参与机制订阅。解决 grain-所有管理人在建设或同步启动装置期间宣布其状态,以便在生命周期启动之前确定订阅。如果使用寿命周期失败,则建筑师处置失败的管理商及其作家,然后宣传故障。

在 <xref:Orleans.Runtime.GrainLifecycleStage.SetupState>期间,经理:

1. 将日记作为定购字节流读取 。
1. 选择存储格式元数据命名的读者。
1. 重建国家名称目录。
1. 每一个登记的耐久状态都重新恢复和重播。
1. 重放完成后完成激活设置 。

<xref:Orleans.Grain.OnActivateAsync*> 和请求在设置成功后观察已恢复的耐久状态,无论 grain 直接来自 <xref:Orleans.Grain>、申请所属基地或 <xref:Orleans.Durable.Journaling.DurableGrain>。 存储读数、格式、编码或错误的数据故障无法激活并保存储存的日记账,以便诊断和恢复。

提供方登记后,可以提供《日刊》服务。每 --grain 日记I/O只开始直接或通过耐久国家依赖关系解决经理问题的启动。 Grains 使用其他持久性模型的激活行为保持其现有的激活行为。

生命周期阶段按顺序执行,在同一阶段的回调有资格同时运行。一个功能读取 `SetupState`后回收状态订阅;一个特性必须在 `OnActivateAsync` 在 <xref:Orleans.Runtime.GrainLifecycleStage.Activate>之前完成。共同设置行动在施工后进行,并在启动之后同时重新使用,因此它们通过提供的背景解决范围状态问题。见 [共享激活集共享设置](../grain-lifecycle.md#shared-activation-setup)。

国家创建属于初始化前的设置。 启动后, `GetOrAddState` 可以解决现有的注册问题; 在更改注册之前立即添加一个缺失的名称失败。 <xref:Orleans.Durable.Journaling.IDurableStateManager.TryGetState*> 提供搜索而不创建。 应用程序读取并突变使用初始化成功后的重建状态。

存储提供者可以任意以字节边界分割读取。 日记格式缓冲不完整的条目, 并且只应用完整定购的记录 。

<a id="custom-and-caller-owned-managers"></a>

### 客户和自调来者和电话用户主管

Grain- 范围主管在解决返回之前注册,标准主管在其 grain- 装有限制的构件器并安装 grain- 远足 `IDurableStateManager` 独立日记账所有人 <xref:Orleans.Durable.Journaling.IJournaledStateManager>自定义 grain 替换的管理人员主管酌情提供合同和登记,并视情况提供合同和登记。 <xref:Orleans.ILifecycleParticipant`1> 用于 <xref:Orleans.Runtime.IGrainLifecycle>,或者直接订阅初始化和关闭回调。这给普通用户 grains、应用程序所属基地,以及 `DurableGrain` 注射的管理人员的恢复和停机生命周期相同。

管理者通过 <xref:Orleans.Durable.Journaling.IJournaledStateManagerFactory.CreateStandalone*> 创建,有明确的 <xref:Orleans.Durable.Journaling.JournalId>,管理人员直接从储存处建立,没有 grain 背景,保留呼叫者的自备初始和处置。这适用于 grain 电话以及运行时间以外的地方。用其附属关系构造国家机器部件,用 <xref:Orleans.Durable.Journaling.IJournaledStateManager.RegisterStateMachine*>注册,在使用回收状态之前等待操作注销标记 <xref:Orleans.Durable.Journaling.IJournaledStateManager.InitializeAsync*> 。用 <xref:Orleans.Durable.Journaling.IJournaledStateManager.TryGetStateMachine*>查询已登记部件。调用者可以通过将所有人登记在 grain-范围化的登记工厂来故意分配生命周期所有权。独立创建使得处理失败独立于环境激活,包括在此输入之后。

Grain- 拥有的管理人员使用现有启动范围来 `GetOrAddState` 和关键部件分辨率。运行时自有的范围控制DI创建的国家组件、其依赖性和管理人的处置。 <xref:Orleans.Durable.Journaling.JournalingHostingExtensions.AddStateMachine*> 登记了该 grain-达标路径的应用国组件。

独立日记所有者手动协调已注册的组件, 并且从不创建或处置DI范围。 他们的呼叫者提供依赖性, 并管理他们一生的生活, 包括任何自有的范围。 重播使用配置好的共享日记服务, 当需要存储格式的编码器时 。 处置所有者停止日记处理和发布日记资源; 部件和依赖性的处理仍然是呼叫者的责任。 持久任务碎片使用这种所有权模式来明确提供它们的身份 。

<a id="mutation-and-write-acknowledgement"></a>

## 变异和写确认

永久收藏在将其编码到模拟收藏之前,先将其操作编码。 因此代码错误使日记缓冲和收藏都保持不变 。

<xref:Orleans.Durable.Journaling.IDurableStateManager.WriteStateAsync*> 从所有指定州收集待处理的条目, 并排队一个存储操作。 受保护的 `DurableGrain.WriteStateAsync` 助手将转发给同一个管理者:

- **附录** 添加编码作业批量原子法。
- **抓图替换** 写下每个已登记流的当前状况,并以原子方式将其作为新日记的生成版本出版。

排队操作的排队共享中, 在写时同时进行同时调用。 存储压缩
请求或待处理的移民选择快照;在快照成功之前,仍需要满足要求。
每个呼叫者都观察其完成或故障。
后期操作处理。

> [!IMPORTANT]
> 存储确认前, 即时突变明显可见。 只有在所需 `WriteStateAsync` 完成后才能将成功恢复到呼叫者手中。 恢复会在新的激活中重建持久性状态 。

<a id="safe-to-commit-staging"></a>

## 安全到承诺的中转

所有互换电话都分享经理的未结日记。 准备可错的工作,外部确认
以及业务-当地数据的拟议产出。
并采用完整的安全到承诺更新的先决条件,同步进行,然后要求普通书写。
Orleans 执行单一个激活线上同步的块。 grain 转转可运行时
任何来电者可以写下
与其他电话发生变异

国家执行《公约》的国家通过
<xref:Orleans.Durable.Journaling.IStateMachine.PrepareWriteAsync*>. 经理人援引
备稿为无能力:已准备好
以同步完成且通过抓取保持有效。
自动完成时, 经理将重新检查所有各州, 以包括所演变异
完全同步的即时通道直接流入捕捉
下 <xref:Orleans.Durable.Journaling.IStateMachine.OnWriteCompleted> 回调后发布
其影响取决于储存确认。

编写报告失败 <xref:Orleans.Durable.Journaling.JournaledStatePreCommitException>
保留了未结状态,即经理
停止工作注册和
取消队列中的操作, 而当前操作会排出到实际存储结果 。

<a id="consistency-and-competing-writers"></a>

## 一致性和相互竞争的作家

Orleans grain 职位安排通常为 grain 身份提供单一的活跃作家。当一个老掉牙或竞争作家到达存储点时,《日刊》储存提供者还使用乐观的同质货币保护日记。

每个存储实例都以首次成功读取或创建来建立其写作的先决条件,然后通过自己的写作来推进。 这一先决条件包括一个不存在的日记:一个找到空日记的作家可以创建它,如果另一个作家先创建它,就会失败。

附加、替换或删除的存储元数据会丢弃 <xref:Orleans.Storage.InconsistentStateException>。国家经理将永久地围住进一步操作,排队的故障工作并要求取消 grain 。失败的运行报告其最初的例外情形。新的激活重播存储的日记以确定持久结果。

设计命令可容忍在应用程序边界进行回溯。 当调用者在网络结果不确定后可以重复命令时使用操作识别符 。

<a id="journal-storage-contract"></a>

## 日记仓储合同

该期刊是可持久状态的真理来源,因此每个 <xref:Orleans.Durable.Journaling.IJournalStorage> 提供者保证以下内容, Orleans 存放处的供应商合规套件核查每一供应商的每项担保:

- **全部或无一内容都写下来。** 一个附件让整个部分在先前的每个段之后都能看到,或者没有看到。替换者将它的全部价值放在日记唯一的部分上,或者该日记保留其早先的内容。 它贯穿于过程和存储崩溃、超时、冲突以及供应商内部的重试。
- **整调限制失败。** 一个超过供应商限额的段除例外不通过调用,任何内容都无法看得见。
- **失败结果。** 明确拒绝的文字留下先前的内容。 含有不确定结果的文字, 如请求发出后暂停使用其整个段或其中任何一段。 重试可以报告原始请求提出后的先决条件冲突; 恢复决定结果。
- **整个被订购的读数为 。** 成功读数返回日记的一个版本:每个部分,直到该版本为止,全部和按写法顺序写。调用者从一个失败的读数中丢弃数据并从一开始恢复。
- **写先决条件。** 一个存储实例在读或创建日记之后写,并通过一个没有丢掉 <xref:System.InvalidOperationException>的例子写出。只有日记仍然符合前提条件时,该日记才能成功;在观察到不在场后,该日记必须仍不在。元数据操作保留内容的先决条件。

直接储存电话员等待每次作业,然后通过同一程序开始另一起。

随后的读物为活跃作家提供历史,同时确认其先决条件。另一个作者对内容的修改产生了 <xref:Orleans.Storage.InconsistentStateException>。冲突后的恢复使用新的存储实例。

附加件至少包含一个字节。 空附件在触摸存储前会丢弃 <xref:System.ArgumentOutOfRangeException> 。 替换后将接受空白值以发布空日记 。

每个供应商都以文件证明其限度:

| 提供商 | 最大的附件 | 最大型替换 |
| --- | --- | --- |
| Azure 布线存储 | 100 MiB (一个附加块) | Azure 块布限制 |
| Azure 表格储存 | 2 MiB (一个实体集团交易) | 分区能力; 行通过一个页眉更新发布 |
| Redis | 整个日记字符串, 包括附件在内, 符合服务器的字符串限制( 默认情况下为512 MB) | 服务器的字符串限制 |
| 亚马逊 S3 | 5GB/每份请求;有条件修改附件将整个世界采购联盟保持在5GB之内 | 5 GB |
| 波动(发展) | 进程内存 | 进程内存 |

<a id="storage-failures"></a>

## 存储故障

失败的附加件、 快速替换, 或者永久删除管理器实例中的围栏 。
排队作业有过失,后来写、删除、登记和启动请求明显不合格。
现有目前状态仍然可以进行飞行通话,直至解除功能完成。 grain 运行时间
启动禁用作为处理失败的一部分。

存储操作在确认失败之前可以承诺。
使用操作识别符或另一种应急功能在新激活中应用和调节结果
机制。

通过 <xref:Orleans.Durable.Journaling.IJournaledStateManagerFactory>创建的独立管理人员的所有者
处置失败的管理器, 为同一管理器创建新的管理器 <xref:Orleans.Durable.Journaling.JournalId>。
构造带有呼叫者提供依赖性的新国家机器组件,并使用这些部件注册
<xref:Orleans.Durable.Journaling.IJournaledStateManager.RegisterStateMachine*>,在恢复之前先初始化
处理程序。 呼叫者还根据失灵部件及其依附情况,将这些部件及其依赖性退休
规定寿命。

正在取消写作的注销符号停止呼叫者等待。 已经排队的写入会继续到它
存储结果, 所以呼叫者在重试命令前对结果进行调节 。

初始化失败时向该尝试的电话员报告错误,使经理无法启动。
调用器可在瞬时后重试 <xref:Orleans.Durable.Journaling.IJournaledStateManager.InitializeAsync*>
恢复所需的格式、编码或支持数据后发生故障或出现故障。
使用同样的注册国家机器,从头开始记账并重放日记。
同时的调用者也参与积极尝试;在启动后可以提供写作和删除
国家登记一旦启动即停止。
一项工作循环任务负责管理人员一生的回收和随后的日记工作。
在尝试失败后,它等待明确初始化请求再重新试。

取消初始回收的自有关闭取消所有启动服务服务员并离开经理
关闭。处置等待自控阅读完成后再发布日记资源。取消
个人初始化呼叫器的标记只结束等待; 其他呼叫器继续自有恢复。

<a id="custom-state-lifecycle"></a>

## 定制状态生命周期

自定义 <xref:Orleans.Durable.Journaling.IStateMachine> 执行共享管理器的单一逻辑执行线条 。
注册工厂选择了由
用于配置日记所有者的相同写格式密钥。
与激活服务相依赖;独立调用器供应代码,使用期适当。
当国家建设时,包括空日记本在内,代码码即可使用。
在重放期间, <xref:Orleans.Durable.Journaling.JournalReplayContext.GetRequiredCommandCodec*> 选择代码器
每个条目的存储格式。

通过 <xref:Orleans.Durable.Journaling.IStateMachine.WritePendingEntries*>同步编码其待更改
或当前内容通过 <xref:Orleans.Durable.Journaling.IStateMachine.WriteSnapshot*>。
在存储确认已抓取 字节后, <xref:Orleans.Durable.Journaling.IStateMachine.OnWriteCompleted*>
执行耐久完成的簿记。 零字写全不进行回调 。

日记的所有人通过删除存储和重置结果,保持功能性操作的发源地
包括当呼叫器取消等待时。 成功删除呼叫 <xref:Orleans.Durable.Journaling.IStateMachine.Reset*>
在完成删除服务员之前。

经理记录第一个写或删除失败,围栏进一步固存,断层
并排队管理经理服务员, 要求 grain 取消。 特性观察他们的写作失败
通过其经营和生命周期所有权,完成自己的运营服务员和资源清理。
独立来电者明确拥有清理设备。 以前被捕获的写作保留了实际存储
由所有人主导的初步恢复和闲置关闭完成
通过正常关闭; 注册写入/ 删除存储取消仍然为终端。

<a id="durable-jobs-shard-retirement"></a>

### 长期就业的艰苦退休

当一个碎片跑者终止时, 包括意外执行失败后, 本地的持久工作
管理人员撤回其可写成和隐藏的推荐文件,并保留清理责任直至
日记硬退休停止了突变进入和等待之前
在一个排队完成屏障上接受操作。 所有权清理然后对干光的工作进行抽样
数,释放人口散块以备发现,并删除空散点日记。

保留退休实例的固定电话排队,对可写文件替换者重新尝试。
退休前确认的退休金在日记中保留,并通过新鲜金银
发现保留了不同的替代情况,而老跑者则完成
清理。 阴道检查, 跳过退休的事例, 本地出版和激活检查
当延迟创建或发现结果到达时, 一次重复的寿命, 即再次出现。 稍后的扫描打开了一个新的
清理工作取消了逐个案件身份的追踪记录。

清理失败被记录下来, 意外执行失败仍然是运行失败。 之前的操作失败是运行失败 。
将废旧的日记予以删除,保留已存在的日记,以在新的实例中恢复。
关闭或清理所有者失败也驱逐和处置退休的当地机构。
记录存储或元数据故障,并允许进行新的发现扫描以恢复持续的工作。

<a id="compaction"></a>

## 压缩

每个供应商在日记跨过配置的存储阈值时报告。 下一个 `WriteStateAsync`:

1. 构建包含州目录和每个活动耐久状态的快照。
1. 原子用快照取代已出版的期刊。
1. 存储确认替换后清除压缩请求。

光谱约束根据供应商门槛重现工作和储存增长。 快速显示的大小仍然与 grain拥有的完全耐久的国家相仿,因此能力测试必须包括热量和大容量 grain 身份。

<a id="retire-a-named-state"></a>

## 退休后的国家

回收保留了不再以当前 grain 类型注册的名称的流。 管理员开始为每个这样的状态设定一个退休宽限期。 默认最少为7天, 配置为 <xref:Orleans.Durable.Journaling.JournaledStateManagerOptions.RetirementGracePeriod>。

在宽限期期间重新使用相同的状态名称, 将其保存的条目重放到新的州例中。 宽限期过后, 稍后的压缩会清除退役流。 因此永久删除可能晚于配置的时期 。

此行为支持了分阶段部署和回滚。 保留以前的格式编码器, 并同时保存已退休流。 当未注册流无法解码为快照时, 格式迁移会暂停 。

宣布每个国家保留每次激活, 即使启动从未进入其内容。 设置声明决定了哪些流仍然在运行 。 声明开始退休 ; 在安装时登记同一名称, 以便在以后的启用中恢复 。

<a id="deactivation-and-shutdown"></a>

## 拆除和关闭

Grain 停止运行将阻止受生命周期约束的国家管理人员的工作循环,启动式处置会释放出资源。 已完成的写作是耐久屏障。 在进行突变的 grain 呼叫中等待所需的每篇文字,在已经写好的大小主机关闭宽限期内等待。 明确日报或独立管理员的所有人可以自行安排关闭和处置。
