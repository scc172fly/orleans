<a id="orleans-clustering-testkit"></a>

# Orleans 集群 TestKit

`Microsoft.Orleans.Clustering.TestKit` 提供可在任意测试框架中复用的成员资格提供程序
符合性测试。它基于 Orleans Core 和
Microsoft.Accordant 构建。违反契约时抛出 `ClusteringConformanceException`。

<a id="factories-and-ownership"></a>

## 工厂与所有权

为**每个直接测试场景**使用隔离的测试夹具。每次工厂调用必须
使用提供的集群 ID 及相同的
后端或服务配置，构造新的 `IMembershipTable`。独立的提供程序实例用于检验
跨实例协调。请分配专用的测试成员资格分区：
夹具拥有这些分区，并在清理阶段删除其中的数据。

```csharp
MembershipTableTestFixture CreateFixture() => new(
    "MyProvider",
    (serviceId, clusterId, cancellationToken) =>
    {
        var owner = CreateProviderResources(serviceId, clusterId);
        return ValueTask.FromResult(
            new MembershipTableTestHandle(owner.Table, owner.DisposeAsync));
    },
    isDeletedAsync: IsMembershipDataDeletedAsync,
    serviceId: "my-provider-conformance");

await CreateFixture().RunAsync(
    (fixture, ct) => new MembershipTableTestRunner(fixture, seed: 17)
        .UpdateRow_TokensCapturedBeforeHeartbeat_CommitStatusChange(ct),
    cancellationToken);

await new MembershipTableModelBasedTestRunner(
    CreateFixture,
    new MembershipTableModelBasedConformanceOptions
    {
        ProviderName = "MyProvider",
        Seed = 17,
        MaxDepth = 3,
        MaxSequenceLength = 3
    }).RunGeneratedConformanceTests(cancellationToken);
```

依据提供程序的配置和
资源所有权规则实现 `CreateProviderResources`。可选的句柄释放器明确拥有这些
资源。还可以使用更简单的同步形式：
`new MembershipTableTestFixture("MyProvider", clusterId => CreateTable(clusterId), IsMembershipDataDeletedAsync)`，
适用于无需按句柄释放资源的情况。另有异步形式的
`(clusterId, cancellationToken) => ValueTask<MembershipTableTestHandle>`
重载可用。

每个构造函数均要求提供签名为
`Func<string, CancellationToken, ValueTask<bool>>` 的 `isDeletedAsync` 委托。应将其实现为针对所给集群 ID 的**原始后端作用域**的只读
探测。只要预置的成员资格数据仍存在，就返回 `false`；
数据已删除，或原生所有者已使其存储永久失效时，返回 `true`。
持久化提供程序可以
通过独立访问查询同一个后端；具有终止语义的提供程序可以
检查其所有者的原生失效状态。在验证完成前，保持该所有者及作用域存活。
传播探测和后端故障。

测试套件首先要求已填充的数据返回 `false`，调用提供程序的
真实删除方法，然后要求删除自身作用域后返回 `true`，这些步骤全部在
释放任何句柄或所有者前执行。探测访问保留原始状态：
直接读取现有存储或原生失效状态，而非初始化
表、替换后端或重置所有者。这样的顺序可以暴露空操作的
删除实现，即便释放所有者本身就会销毁后端数据。

`InitializeAsync` 在 `ClusterId` 中创建 A1/A2，在 `OtherClusterId` 中创建 B1；
`First`、`Second` 和 `OtherCluster` 公开这三个不同的提供程序。
并发调用初始化的调用方共享同三个已初始化句柄；
每个排队的调用方可以取消自身的等待。
第二个集群 ID 是第一个加上后缀，用来检验
具有相同集群名称前缀的存储键是否相互隔离。
`CreateAdditionalHandleAsync` 仅接受这两个拥有且仍存活的集群作用域。
句柄注册和清理阶段的所有权快照共用同一生命周期锁。
已准入的工厂调用、提供程序初始化、终止性删除、原生探测
以及迟到的所有者释放操作，在其实际任务完成前
始终受所有权管理。调用方取消只结束该调用方的
等待；工厂及提供程序初始化仍保持原有
所有者存活。如果资源获取在释放或历史退役后才完成，
则先释放其返回的所有者，再报告生命周期错误。清理阶段会等待
这些已准入操作完成，然后才删除作用域或释放共享所有者。
附加句柄必须显式初始化。`RunAsync` 执行初始化、
运行和清理，同时保留主要故障。清理只删除
夹具自己的集群分区，实际删除操作完成后尝试释放每个所有者。
清理派发在调用方线程之外运行，
因此即使回调同步阻塞，也遵守调用方 30 秒的等待上限。超时会
保留所有者及共享清理任务直至操作完成；后续
`DisposeAsync` 调用等待同一任务。超时异常的数据包含
`ClusteringTestKit.CleanupCompletion`，迟到的清理故障以
`ClusteringTestKit.CleanupFailure` 附加。永不完成的操作会保留
其所有者；上限约束的是调用方的等待，资源释放则等待
操作实际完成。已删除的
作用域会退役；清理直接释放其所有者。如果删除请求
在调用后失败，也会使其句柄退役，因为它可能已
提交。后续历史使用具有全新所有者的新夹具。
普通成员资格操作接收其场景的取消令牌。
夹具跟踪其生命周期回调直至完成。句柄释放器根据实际 SDK 的关闭、中止或排空
契约释放其声明拥有的资源，
由此产生的任何基础设施故障仍可观察到。
使用不含机密的提供程序标签：诊断信息包括标签、集群 ID、不透明
令牌、标识和不匹配的持久化字段。

<a id="independently-discoverable-direct-guarantees"></a>

## 可独立发现的直接保证

在测试框架中将以下每个运行器方法公开为单独的 fact 或测试。所有方法都返回
`Task`，并接受 `CancellationToken cancellationToken = default`。每个提供程序
执行相同的行为断言。

| ID | 公共方法 |
|---|---|
| G01 | `InsertRow_CurrentTableVersion_CommitsExactlyOneVersion` |
| G02 | `UpdateRow_CurrentTokens_CommitsExactlyOneVersion` |
| G03 | `Reads_SameVersion_PreservesRetainedCanonicalFields` |
| G04 | `Reads_MaySkipCommittedVersions_WithoutSkippingHistoryValidation` |
| G05 | `Lifecycle_DeadRemainsTerminalAfterCompaction_SuccessorUsesNewGeneration` |
| G06 | `UpdateIAmAlive_OwnerWrites_PreserveCanonicalFieldsAndTableVersion` |
| G07 | `UpdateRow_TokensCapturedBeforeHeartbeat_CommitStatusChange` |
| G08 | `UpdateRow_TokensCapturedBeforeHeartbeat_CommitVoteChange` |
| G09 | `Handles_IndependentlyConstructed_ShareCommittedBackingState` |
| G10 | `InsertRow_StaleTableToken_ReturnsFalseWithoutSideEffects` |
| G11 | `UpdateRow_StaleTableTokenWithCurrentRowToken_ReturnsFalseWithoutSideEffects` |
| G12 | `UpdateRow_StaleSnapshotAfterSameRowCommit_ReturnsFalseWithoutSideEffects` |
| G13 | `InsertRow_DuplicateIdentity_ReturnsFalseWithoutSideEffects` |
| G14 | `UpdateRow_MissingIdentityWithRealToken_ReturnsFalseWithoutSideEffects` |
| G15 | `ReadAll_SelectsPresentAndAbsentIdentities` |
| G16 | `Reads_RetainedObjectsRemainUnchangedAfterLaterWrites` |
| G17 | `InsertRow_MutatingInputAndSuspectList_DoesNotMutateStoredState` |
| G18 | `UpdateRow_MutatingInputAndSuspectList_DoesNotMutateStoredState` |
| G19 | `Reads_MutatingReturnedEntryAndSuspectList_DoesNotMutateStoredState` |
| G20 | `ConcurrentCrossRowUpdates_SharedTableVersion_HaveExactlyOneWinner` |
| G21 | `ConcurrentReadAll_ReturnsOnlyAtomicCommittedViews` |
| G23 | `InitializeMembershipTable_RepeatedWithData_PreservesCommittedState` |
| G24 | `Clusters_SharedBackendWithOverlappingSiloAddresses_AreIsolated` |
| G25 | `CleanupDefunctSiloEntries_RemovesOnlyStrictlyOldDeadRows` |
| G26 | `DeleteMembershipTableEntries_DeletesOwnClusterAndPreservesOtherCluster` |
| G27 | `DeleteMembershipTableEntries_DifferentClusterId_NeverDeletesConfiguredCluster` |

套件公开 26 个直接用例以及生成式符合性测试。

生成式 fact 按惯例命名为
`MembershipTable_ModelBased_GeneratedConformance`；它调用
`MembershipTableModelBasedTestRunner.RunGeneratedConformanceTests`。
生成的用例通过 Accordant 的
异步生命周期钩子等待夹具初始化和释放，每个用例均使用全新夹具及隔离作用域。
公共运行器执行完整的生成式套件。仓库中托管的
系统目标测试将相同的 seed-17 清单分布到四个 xUnit 用例中
（240、240、240 和 239 条历史），保留每条历史及操作数量。
生成模型包含 18 种操作。对存在和不存在的条目的
观察通过 `TryGet` 从 `ReadAllAsync` 快照中选择条目。

<a id="comparison-and-protocol-rules"></a>

## 比较及协议规则

规定的历史从正常 Silo 注册开始：新成员资格
条目的怀疑历史为空。这些历史通过对现有行执行规范更新，建立并修改
怀疑投票。
向前更新会修改状态和怀疑投票，同时保留激活实例的
标识、终结点、主机及 Silo 名称、启动时间和部署元数据。

不可变观察结果捕获条目的所有公共持久化字段、嵌套的
被怀疑标识及时间、完整的终结点及世代标识、行 ETag，以及
整数版本及表 ETag。提供程序观察结果比较规范字段及
表版本和 ETag，独立于原始 IAmAliveTime 和提供程序定义的行
ETag。保留对象的独立性检查还比较已捕获对象的存活信息
及行元数据。版本相同时，历史要求保留所有非 Dead
行和所有保留的规范字段，同时允许此前处于 Dead 状态的行被
裁剪。IAmAliveTime 独立更新，且不保证一致性；行 ETag
是提供程序的不透明元数据，可能随心跳或物理重写而变化。
null 与空怀疑列表以及怀疑项或行的枚举顺序会被
规范化；缺失的可选角色名称采用 null 或空值表示时也会规范化。
可选 Azure 部署元数据被显式初始化为
空角色和零可用区，但每次比较仍包含所有这些字段。

每次成功的协议写入均提供真正的当前 `TableVersion.Next()`，
并且版本必须恰好前进一次。ETag 是不透明值。成员资格协议提供
合法的下一版本整数和向前的状态转换。
规范更新会原子地验证表 ETag 并替换现有
行。提供程序还可以验证规范行令牌；如果表版本 CAS 已保护更新，也可以忽略
提供的行 ETag。任何附加的
行保护条件在仅发生心跳活动时仍应满足。过期表令牌测试
在竞争的跨行提交后使用新读取的行元数据，在同一行提交后使用原始
快照输入。所有未变更的规范条目字段均得到
保留。整行写入可以将心跳覆盖为较早的值。
不同读取可能跳过已提交的版本。更新的完整视图可能省略
此前存活的标识，确立其终止性死亡，即使观察者
错过了其显式 Dead 状态转换也是如此。历史仍会保留终止标识，
即使在压实之后。

仅清理 Dead 行时，可以保留版本及表 ETag，也可以使用新表 ETag 进行原子的 +1
提交。存在多个符合条件的行时，套件接受
批处理，并要求版本总增量介于零与
被移除的行数之间。所有保留的规范字段保持不变。
空清理保留规范观察结果及表版本和 ETag。
清理场景保留一个时间戳恰好位于截止值的 Dead 行，再以相同
截止值重复清理，要求无操作；随后将截止值推进一个 tick 并要求删除该行。
准备阶段在将目标行转换为 Dead 之前，通过其存活的所有者发布每次心跳，
并在该转换中提供相同的时间戳。
清理资格基于这些受控发布及规范启动时间和投票
时间戳，允许原始心跳观察存在延迟。
重启在相同终结点使用严格更新的世代。生成
模型选择合法的向前生命周期操作。

删除自身集群会移除该集群的数据，并保留其他集群。
删除结束已存储的历史。G26 验证原生删除及其他
集群的完整视图。G27 使用已填充的外部集群，全程验证
已配置集群的完整视图。
对于外部集群 ID，作用域限定的提供程序可以保持该作用域不变，或
抛出参数名为 `clusterId` 的 `ArgumentException` 拒绝请求。套件验证拒绝后
完整保留规范状态；作用域覆盖整个后端的提供程序也可以删除
所请求的外部作用域。若保留外部作用域，G27 检查其
规范视图，再测试其自身作用域删除。原生探测确定
哪些作用域仍可读取；退役句柄仅接受所有者释放操作。

每个拥有成员资格行的 Silo 通过一次不预读的列写入发布自身存活状态。
心跳输入包含标识和时间戳。TestKit 通过一个所有者，在其行存活期间依次发布固定的
整秒 UTC 时间 t1、t2、t2。
每次调用均保留规范成员资格字段及表版本和 ETag。
返回的行元数据可以变化。读取可能滞后或显示较早的存活值。
生成的心跳操作
针对每个标识使用固定的所有者句柄，且不超出该行的存活期。

提供程序原生测试验证每次周期性心跳的存储操作预算：
**零前置读取、零比较并交换操作，以及一次
不预读的存活状态写入**。TestKit 的观察读取在提供程序调用前后执行，
以检查结果字段；原生 SDK 插桩确定操作
次数。运行时快照合并保留观察到的最大存活时间值。
提供程序原生测试还验证，仅发生心跳活动后使用原始输入进行规范更新，
只需一次变更尝试，提供程序 API 内部不存在由心跳驱动的
重读或重试。通用场景确定该 API 边界的
结果；SDK 插桩确定尝试次数。

G07 和 G08 捕获行 ETag 和表版本，执行所有者心跳，
再使用原始输入提交状态或怀疑投票变更。每次更新
均成功一次，版本恰好增加 1。这些用例使心跳
不干扰规范写入的性质可以在写入边界观察到。整行有效载荷
可能覆盖较新的已存储心跳；模型中的所有者发布时间戳
描述生成的输入，与返回的原始存活值无关。

成功的向前更新会刷新现有怀疑项的时间戳，并使用空列表或 null 清除
已填充的怀疑列表。
缺失行检查包括 Dead 行压实之后的延迟更新，
同时检查过期令牌及新鲜的表版本候选值。周期性
心跳在所有者的存活行生命周期内完成；原生缺失行
错误和基础设施异常向外传播。

并发测试使用实际构建的就绪、开始和完成同步门、精确的获胜者计数，
以及针对已知前后历史的即时逐观察检查。
ReadAll 场景让读取者与向前更新和 Dead 行清理并发竞争，
包括行从存在到在快照中缺失的转换。
清理场景通过两个句柄检查清理的幂等性，并接受
版本不变或一次原子递增。提供程序原生测试覆盖
相互重叠的清理尝试及其争用和错误行为；运行时
清理代理记录故障并安排后续尝试。
`concurrencyRowCount`（默认 128，
范围 3–10000）限制多行工作负载，不改变任何断言。
适配器必须选择安全且跨越其后端实际分页
或流式传输边界的行数。应保留显式触发
这些边界的提供程序特定分页测试。
准备过程先进行一次初始空全量快照读取，再对每行执行一次 `InsertRowAsync` 调用和
一次 `ReadAllAsync` 验证。每次验证获取下一次插入所需的当前
表 ETag，并将已插入行的规范字段与
独立构建的输入对照。最后两个完整视图与
所有独立预期的规范条目比较。

对于 N 行，这意味着 N 次返回 bool 的插入、N+3 次全量读取，以及
N(N+1)/2 + 2N 条返回的成员资格行。N=4096 时，准备阶段返回 8,398,848 行；
N=1001 时返回 503,503 行。提供程序特定的插桩测量
原生请求、分页和重试成本。

Accordant 利用转换覆盖生成并执行操作序列。
要求的受约束前缀会达到跨行及同一行提交后的过期表快照、
心跳之后使用原始令牌的更新，以及单行和多行 Dead 行压实后
创建后继实例的情形。每个生成用例获取全新作用域；
执行上下文独立于抽象模型保存真实的令牌历史。
`DeleteCluster` 是终止性操作：其结果包含已验证的删除证据，
并结束该历史的所有操作。下一个用例构造新的夹具
及其所有者。生成的清理使用晚于所有生成的
时间戳的截止值，使预期的 Dead 行移除不受心跳滞后
或覆盖的影响。在模型记录版本增量之前，会检查清理结果的范围并验证其仍保留的规范行。
最终执行检查
要求覆盖全部十八种操作。自测试使用独立判定逻辑检验两种清理
策略，分别检查规范行、
源自表版本的行令牌，以及仅使用表 CAS 时的物理行元数据。
自测试也覆盖交替出现的存活状态滞后、整行心跳覆盖、低成本
心跳的保留行为，以及故意违反规范字段或表令牌的情形。
任何夹具清理失败都会在另一个用例或
重放获取所有者之前停止生成式运行。原始用例故障和清理诊断信息
均被保留，可一起查看。
