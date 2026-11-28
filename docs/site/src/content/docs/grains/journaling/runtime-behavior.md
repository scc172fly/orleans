---
title: Journaling 运行时行为与一致性
description: 了解 Orleans Journaling 的激活、写入、恢复、压实、并发及故障语义。
ms.date: 10/19/2026
ms.topic: conceptual
---

<a id="journaling-runtime-behavior-and-consistency"></a>

# Journaling 运行时行为与一致性

Orleans Journaling 为每个 Grain 标识分配一个 <xref:Orleans.Durable.Journaling.JournalId>，并为每个具名持久化状态分配一个日志流。状态管理器将该日志的恢复工作与存储工作串行化。

<a id="activation-and-recovery"></a>

## 激活与恢复

通过 <xref:Orleans.Durable.Journaling.JournalingHostingExtensions.AddJournaling*> 注册的标准 Grain 作用域 <xref:Orleans.Durable.Journaling.IDurableStateManager> 在使用激活实例的 <xref:Orleans.Runtime.IGrainContext> 构造时，会在依赖解析返回之前自行注册到 Grain 生命周期。解析键控持久化状态也会解析此管理器。每个激活实例的注入状态和功能组件共享一个管理器。管理器的注册表负责状态的创建和绑定，因此通过 <xref:Orleans.Durable.Journaling.IDurableStateManager.GetOrAddState*>、其类型化扩展辅助方法或键控注入发起的兼容请求，均返回同一实例。

构造函数注入在生命周期启动前注册持久化状态。Orleans 执行完 Grain 构造函数并设置 <xref:Orleans.Runtime.IGrainContext.GrainInstance>，然后执行任何共享激活初始化操作，之后才调用 Grain 对象的 `Participate` 方法并启动生命周期。功能组件可以在这些同步操作中首次解析管理器、声明额外的激活作用域状态，并注册自身的生命周期参与者。所有订阅均在生命周期启动之前建立。

标准管理器只参与一次生命周期，支持普通的 <xref:Orleans.Grain> 子类、应用程序自定义基类、<xref:Orleans.IGrainBase> 实现以及便捷基类 <xref:Orleans.Durable.Journaling.DurableGrain>。其绑定 Grain 的构造函数通过现有生命周期参与机制进行订阅。在构造期间或同步激活初始化期间解析 Grain 所拥有的管理器并声明状态，确保生命周期启动之前已建立订阅。如果生命周期订阅失败，构造函数会释放失败的管理器及其写入器，然后传播该故障。

在 <xref:Orleans.Runtime.GrainLifecycleStage.SetupState> 阶段，管理器执行以下操作：

1. 将日志读取为有序字节流。
1. 选择存储格式元数据所指定的读取器。
1. 重建状态名称目录。
1. 重置并重放每个已注册的持久化状态。
1. 重放完成后结束激活初始化。

无论 Grain 直接继承 <xref:Orleans.Grain>，还是继承应用程序自定义基类或 <xref:Orleans.Durable.Journaling.DurableGrain>，初始化成功后，<xref:Orleans.Grain.OnActivateAsync*> 和请求都能观察到恢复出的持久化状态。存储读取、格式、编解码器或数据格式不正确所导致的故障会使激活失败，并保留已存储的日志以供诊断和恢复。

注册提供程序后，Journaling 服务即可使用。只有直接或通过持久化状态依赖项解析管理器的激活实例，才会开始对应 Grain 的日志 I/O。采用其他持久化模型的 Grain 保持其现有的激活行为。

生命周期各阶段依次执行，同一阶段的回调可以并发运行。需要读取恢复状态的功能组件，应订阅 `SetupState` 之后的阶段；必须在 `OnActivateAsync` 之前完成的功能组件，应订阅 <xref:Orleans.Runtime.GrainLifecycleStage.Activate> 之前的阶段。共享初始化操作在构造之后执行，并由多个激活实例并发复用，因此应通过提供的上下文解析作用域状态。参阅[共享激活初始化](../grain-lifecycle.md#shared-activation-setup)。

状态创建应在初始化开始前的设置阶段进行。初始化开始之后，`GetOrAddState` 可以解析现有注册项；如果试图添加缺失的名称，会在改变注册表之前立即失败。<xref:Orleans.Durable.Journaling.IDurableStateManager.TryGetState*> 提供不创建状态的查找方式。初始化成功后，应用程序读取和修改的是重建出的状态。

存储提供程序可以在任意字节边界拆分读取结果。日志格式会缓冲不完整的条目，仅应用完整且有序的记录。

<a id="custom-and-caller-owned-managers"></a>

### 自定义管理器和调用方拥有的管理器

Grain 作用域的管理器在依赖解析返回之前完成生命周期注册。标准管理器在绑定 Grain 的构造函数中完成注册，并同时实现面向 Grain 的 `IDurableStateManager` 和独立的日志所有者接口 <xref:Orleans.Durable.Journaling.IJournaledStateManager>。替换标准管理器的自定义 Grain 管理器应按需提供这两个约定及其注册。它们在构造函数或注册工厂中，通过将自身针对 <xref:Orleans.Runtime.IGrainLifecycle> 的 <xref:Orleans.ILifecycleParticipant`1> 注册到生命周期，或者直接订阅初始化及关闭回调来完成注册。这样，普通 Grain、应用程序自定义基类和 `DurableGrain` 所注入的管理器都具有相同的恢复和关闭生命周期。

通过 <xref:Orleans.Durable.Journaling.IJournaledStateManagerFactory.CreateStandalone*> 使用显式 <xref:Orleans.Durable.Journaling.JournalId> 创建的管理器，以及没有 Grain 上下文、直接基于存储构造的管理器，仍由调用方负责初始化和释放。这在 Grain 调用内部和运行时外部均适用。构造状态机组件及其依赖项，使用 <xref:Orleans.Durable.Journaling.IJournaledStateManager.RegisterStateMachine*> 注册组件，并在使用恢复状态之前，携带操作的取消令牌等待 <xref:Orleans.Durable.Journaling.IJournaledStateManager.InitializeAsync*> 完成。使用 <xref:Orleans.Durable.Journaling.IJournaledStateManager.TryGetStateMachine*> 查找已注册组件。调用方可以通过在 Grain 作用域注册工厂中将日志所有者注册到生命周期，显式指定生命周期的所有权。独立创建方式使故障处理不依赖当前环境中的激活实例，即便进行了此类注册也是如此。

Grain 拥有的管理器使用现有激活作用域进行 `GetOrAddState` 和键控组件解析。运行时拥有的作用域负责释放 DI 创建的状态组件、其依赖项和管理器。<xref:Orleans.Durable.Journaling.JournalingHostingExtensions.AddStateMachine*> 为这种面向 Grain 的访问路径注册应用程序状态组件。

独立日志所有者协调手动注册的组件，从不创建或释放 DI 作用域。其调用方提供组件依赖项，并负责它们的生命周期，包括调用方拥有的任何作用域。重放需要某种存储格式的编解码器时，使用已配置的共享日志服务。释放日志所有者会停止日志处理并释放日志资源；组件和依赖项的释放仍由调用方负责。Durable Jobs 分片采用这一所有权模型管理显式提供的状态。

<a id="mutation-and-write-acknowledgement"></a>

## 修改与写入确认

持久化集合先对操作进行编码，再将操作应用到内存中的集合。因此，编解码器发生故障时，日志缓冲区与集合均保持不变。

<xref:Orleans.Durable.Journaling.IDurableStateManager.WriteStateAsync*> 收集所有具名状态的待处理条目，并将一个存储操作加入队列。受保护的 `DurableGrain.WriteStateAsync` 辅助方法将调用转发给同一管理器：

- **追加**以原子方式添加已编码的操作批次。
- **快照替换**写入每个已注册日志流的当前状态，并以原子方式将其发布为新一代日志。

写入尚在队列中时发起的并发调用共享该排队操作。存储压实请求或待执行的迁移会选择生成快照；这一要求一直保留到快照成功为止。每个调用方都能观察到操作的完成或失败。在存储操作开始之后发起的调用，由后续操作处理。

> [!IMPORTANT]
> 内存中的修改在存储确认之前就已可见。只有所需的 `WriteStateAsync` 完成之后，才向调用方返回成功。恢复过程会在新的激活实例中重建持久化状态。

<a id="safe-to-commit-staging"></a>

## 安全提交的暂存方式

所有交错执行的调用方共享管理器的待写日志。先在操作局部数据中准备可能失败的工作、外部确认和拟输出的结果。在最后一次准备操作的等待结束后，检查相关前置条件，同步应用完整且可以安全提交的更新，再请求一次普通写入。Orleans 在单个激活实例线程上执行该同步代码块。操作等待时，其他 Grain 执行轮次可能运行，因此在每个等待点都应确保共享状态可以安全提交。任何调用方的写入都可能包含其他调用暂存的修改。

状态实现通过 <xref:Orleans.Durable.Journaling.IStateMachine.PrepareWriteAsync*> 建立异步持久化前置条件。管理器在捕获追加内容或快照之前调用此回调。准备过程具有幂等性：已经准备好的前置条件会同步完成，并在捕获期间持续有效。异步操作完成后，管理器重新检查所有状态，以纳入交错执行轮次暂存的修改。当一轮完整的同步就绪检查通过后，立即进入捕获阶段。随后，<xref:Orleans.Durable.Journaling.IStateMachine.OnWriteCompleted> 回调发布依赖存储确认的效果。

准备失败时报告 <xref:Orleans.Durable.Journaling.JournaledStatePreCommitException>，其内部异常为原始故障。待处理状态仍被保留，管理器仍可使用，后续写入会重试准备过程。关闭时停止接纳新工作并取消排队操作，当前操作则继续执行直至得到实际存储结果。

<a id="consistency-and-competing-writers"></a>

## 一致性与竞争写入者

Orleans 的 Grain 放置机制通常为一个 Grain 标识提供一个活动写入者。日志存储提供程序还使用乐观并发控制，以便在过期写入者或竞争写入者访问存储时保护日志。

每个存储实例通过首次成功读取或创建日志确立写入前置条件，随后通过自己的写入推进该条件。这个前置条件也涵盖日志不存在的情况：恢复时没有日志的写入者可以创建日志；若其他写入者已先行创建，则该操作失败。

使用过期存储元数据进行追加、替换或删除会引发 <xref:Orleans.Storage.InconsistentStateException>。状态管理器永久禁止后续操作，使排队工作以异常结束，并请求停用 Grain。失败的操作报告原始异常。新的激活实例重放已存储的日志，以确定持久化结果。

应将命令设计为能够容忍应用程序边界上的重试。如果调用方可能在网络操作结果不确定后重复命令，则使用操作标识符。

<a id="journal-storage-contract"></a>

## 日志存储契约

日志是持久化状态的事实来源，因此每个 <xref:Orleans.Durable.Journaling.IJournalStorage> 提供程序都保证以下行为，Orleans 仓库中的提供程序符合性测试套件会针对每个提供程序验证各项保证：

- **全有或全无的写入。** 追加操作要么使整个新段在所有先前的段之后可见，要么完全不可见。替换操作要么将整个新值发布为日志的唯一段，要么保留日志先前的内容。在进程和存储崩溃、超时、冲突以及提供程序内部重试的情况下，此保证仍成立。
- **超限导致整个调用失败。** 超过提供程序限制的段会使调用以异常失败，该段的任何部分都不会变得可见。
- **故障结果。** 明确被拒绝的写入保留先前的内容。结果不确定的写入（例如请求发送后发生超时）要么应用了整个段，要么完全没有应用。原始请求已提交之后，重试仍可能报告前置条件冲突；通过恢复确定结果。
- **完整、有序的读取。** 成功的读取返回日志的一个版本，即截至该版本写入的全部段，且每个段完整并按写入顺序排列。调用方丢弃读取失败时获得的数据，从头进行恢复。
- **写入前置条件。** 存储实例在读取或创建日志之后进行写入；通过既未读取也未创建日志的实例写入，会引发 <xref:System.InvalidOperationException>。只有日志仍满足该前置条件时，写入和后续读取才会成功；如果此前观察到日志不存在，则日志必须仍然不存在。元数据操作不会改变内容前置条件。

直接调用存储的调用方应等待每个操作结束，再通过同一实例开始另一操作。不同实例通过存储前置条件协调。

后续读取为活动写入者提供历史记录，同时验证其前置条件。其他写入者更改内容会导致 <xref:Orleans.Storage.InconsistentStateException>。发生冲突后，使用新的存储实例进行恢复。

追加内容至少包含一个字节。空追加会在访问存储之前引发 <xref:System.ArgumentOutOfRangeException>。替换操作接受空值，以发布空日志。

每个提供程序均记录其限制：

| 提供程序 | 最大追加大小 | 最大替换大小 |
| --- | --- | --- |
| Azure Blob Storage | 100 MiB（一个追加块） | Azure 块 Blob 的限制 |
| Azure Table Storage | 2 MiB（一个实体组事务） | 分区容量；通过一次头部更新发布各行 |
| Redis | 包含本次追加的整个日志字符串必须符合服务器的字符串大小限制（默认为 512 MB） | 服务器的字符串大小限制 |
| Amazon S3 | 每个请求 5 GB；采用条件重写方式的追加操作使整个 WAL 不超过 5 GB | 5 GB |
| 易失性存储（开发用） | 进程内存 | 进程内存 |

<a id="storage-failures"></a>

## 存储故障

追加、快照替换或删除失败后，该管理器实例会被永久隔离。排队操作以异常结束，之后的写入、删除、注册和初始化请求明确失败。停用完成之前，进行中的调用仍可访问现有内存状态。Grain 运行时会启动停用过程，作为故障处理的一部分。

存储操作可能已提交，但其确认消息随后丢失。应将失败的写入视为结果不确定的应用程序操作，并在新的激活实例中使用操作标识符或其他幂等机制核对结果。

通过 <xref:Orleans.Durable.Journaling.IJournaledStateManagerFactory> 创建的独立管理器，其所有者应释放失败的管理器，并为相同的 <xref:Orleans.Durable.Journaling.JournalId> 创建新的管理器。使用调用方提供的依赖项构造新的状态机组件，利用 <xref:Orleans.Durable.Journaling.IJournaledStateManager.RegisterStateMachine*> 进行注册，然后在恢复处理之前完成初始化。调用方还应根据分配的生命周期退役失败的组件及其依赖项。

取消写入的取消令牌会停止调用方的等待。已经排队的写入仍将继续执行直至得到存储结果，因此调用方应在重试命令之前核对该结果。

初始化失败会向该次尝试的调用方报告错误，并使管理器保持未初始化状态。发生暂时性故障后，或恢复所需的格式、编解码器、底层数据后，调用方可以重试 <xref:Orleans.Durable.Journaling.IJournaledStateManager.InitializeAsync*>。每次尝试都重置恢复过程的内部记录，并使用相同的已注册状态机从头重放日志。并发调用方共享当前正在执行的尝试；初始化成功后才能写入和删除。一旦初始化开始，状态注册便一直保持关闭。一个工作循环任务在管理器的整个生命周期内负责恢复尝试和后续日志工作。尝试失败后，该任务等待显式初始化请求，才会重试。

如果所有者关闭时取消了初始恢复，则所有初始化等待方均被取消，管理器进入停止状态。释放操作等待其负责的读取完成，然后才释放日志资源。取消某一个初始化调用方的令牌仅终止该调用方的等待；由管理器负责的恢复继续为其他调用方执行。

<a id="custom-state-lifecycle"></a>

## 自定义状态生命周期

自定义 <xref:Orleans.Durable.Journaling.IStateMachine> 实现共享管理器的单个逻辑执行线程。将命令编解码器作为构造函数依赖项提供。注册工厂选择键与日志所有者配置所用写入格式键相同的编解码器。激活实例拥有的状态工厂从激活实例的服务中解析这些依赖项；独立调用方提供具有适当生命周期的编解码器。状态构造时编解码器即可使用，即使日志为空也是如此。在重放期间，<xref:Orleans.Durable.Journaling.JournalReplayContext.GetRequiredCommandCodec*> 根据每个条目存储的格式选择编解码器。

状态通过 <xref:Orleans.Durable.Journaling.IStateMachine.WritePendingEntries*> 同步编码待处理更改，或者通过 <xref:Orleans.Durable.Journaling.IStateMachine.WriteSnapshot*> 同步编码当前内容。存储确认捕获的字节之后，<xref:Orleans.Durable.Journaling.IStateMachine.OnWriteCompleted*> 负责持久化完成状态的记录管理。零字节写入在不调用该回调的情况下完成。

在删除操作得出存储和重置结果之前，日志所有者保持功能组件操作处于静止状态，即使调用方取消等待也一样。删除成功后，在完成删除等待方之前，调用 <xref:Orleans.Durable.Journaling.IStateMachine.Reset*>。

管理器记录首次写入或删除故障，禁止进一步持久化，使当前及排队的管理器等待方以异常结束，并请求停用 Grain。功能组件观察其写入故障，并通过其对操作及生命周期的所有权，结束自身操作的等待方及完成资源清理。独立调用方明确负责该清理工作。先前已经捕获的写入保留实际存储结果和确认记录。所有者取消初始恢复以及空闲时关闭，均通过正常关闭流程完成；已经接纳的写入或删除操作若发生存储取消，仍属于终止性故障。

<a id="durable-jobs-shard-retirement"></a>

### Durable Jobs 分片退役

当分片运行器终止时（包括执行器意外失败之后），本地 Durable Jobs 管理器撤销该分片的可写引用和缓存引用，并持续负责清理直至释放完成。日志化分片退役时关闭修改操作准入，并在队列中的完成屏障处等待先前已接纳的操作。所有权清理随后获取排空后的作业数量，释放包含作业的分片以供发现，并删除空分片的日志。

仍持有正在退役的实例的调度调用会针对可写替代实例重试。退役前已经确认的排队作业仍保留在日志中，并在发现时通过新的规范实例恢复。旧运行器完成清理期间，发现机制保留不同的替代实例。规范实例查找会跳过正在退役的实例；延迟的创建或发现结果返回时，本地发布与激活再次检查实例的生命周期。逐出实例后，后续扫描会打开新实例。清理操作按实例标识移除跟踪项。

清理故障会记录到日志；执行器意外故障仍作为运行器的故障保留。先前的日志 I/O 故障会禁止删除，保留已持久化的日志以便新实例恢复。关闭或所有权清理失败也会逐出并释放已退役的本地实例。排查记录的存储或元数据故障，并允许新一轮发现扫描恢复已持久化的工作。

<a id="compaction"></a>

## 压实

当日志超过已配置的存储阈值时，各提供程序会报告这一情况。下一次 `WriteStateAsync` 执行以下操作：

1. 构建包含状态目录和每个活动持久化状态的快照。
1. 以原子方式用快照替换已发布的日志。
1. 存储确认替换之后，清除压实请求。

压实依据提供程序的阈值限制重放工作量和存储增长。快照大小仍随 Grain 拥有的完整持久化状态的大小而增长，因此容量测试必须覆盖高访问频率和状态体量大的 Grain 标识。

<a id="retire-a-named-state"></a>

## 退役具名状态

恢复时会保留名称不再由当前 Grain 类型注册的日志流。管理器为每个此类状态开始一个退役宽限期。默认最短时间为七天，通过 <xref:Orleans.Durable.Journaling.JournaledStateManagerOptions.RetirementGracePeriod> 配置。

在宽限期内重新引入相同状态名称，会将保留的条目重放到新的状态实例。宽限期结束后，后续压实会移除退役的日志流。因此，永久移除可能晚于配置的期限。

此行为支持分阶段部署和回滚。只要退役的日志流仍存在，就应保持旧格式编解码器可用。如果未注册的日志流无法解码为快照，格式迁移会暂停。

每次激活时都应声明要保留的所有状态，即使该激活实例从不访问其内容。初始化声明决定哪些日志流保持活动。省略声明会开始退役；在后续激活的初始化过程中注册同一名称，可以在宽限期内恢复该状态。

<a id="deactivation-and-shutdown"></a>

## 停用与关闭

Grain 停用会停止绑定生命周期的状态管理器的工作循环，激活作用域释放时会释放管理器资源。已完成的写入构成持久化屏障。在执行修改的 Grain 调用期间，应等待每个必需的写入完成，并为已在进行的写入设置足够的宿主关闭宽限时间。使用显式日志或独立管理器的所有者应自行安排关闭和释放。
