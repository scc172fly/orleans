<a id="orleans-clustering-testkit"></a>

# Orleans 组合 TestKit

`Microsoft.Orleans.Clustering.TestKit` 供应可再使用的成员提供商
任何测试框架的测试框架的符合性测试。 Orleans 核心和核心
Microsoft.Accordant违反合同行为 `ClusteringConformanceException`.

<a id="factories-and-ownership"></a>

## 工厂和所有权

每个工厂必须使用独立固定装置 **,以每个直接假设情景**。
新建 `IMembershipTable`,使用所提供集束号编号和相同的
后端/服务配置。
指定专用的测试会籍分割:
固定装置在撕毁时拥有并删除数据。

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

执行 `CreateProviderResources` 使用提供者的配置和
资源所有权规则。
更简单同步
`new MembershipTableTestFixture("MyProvider", clusterId => CreateTable(clusterId), IsMembershipDataDeletedAsync)`
当不需要逐手资源处置时, 工作即可。
`(clusterId, cancellationToken) => ValueTask<MembershipTableTestHandle>` 超载
也可用。

每个建筑商都需要有 `isDeletedAsync` 签名的代表
`Func<string, CancellationToken, ValueTask<bool>>`. 执行它作为只读
所提供集束 ID **原始支持范围**的探测。返回 `false`
种子成员数据仍保留,当数据删除时 `true` 或
本地所有者已将其商店彻底失效。
通过独立访问查询相同的后端;终端供应商可以
检查业主的本地无效状态。 保持所有人和范围的生命生命
直到验证完成。 Propagate 探测器和后端故障 。

套件首先需要 `false` 数据,并引用服务供应商的
实际删除方法, 然后要求 `true` 自己范围删除, 全部之前
处理任何控管或所有者。
直接读取现有储存或本地原封存无效,而不是初始化
表格,替换后端,或重置所有者。此顺序显示无操作
即使当所有者处置会破坏支持数据时,也删除。

`InitializeAsync` 在 `ClusterId` 和 `OtherClusterId`中创建 A1/A2;
`First`、 `Second`和 `OtherCluster` 暴露了这些不同的供应商。
同时初始化呼叫器拥有相同的三个初始化控点;
每个排队的呼叫员可以取消自己的等候时间 。
第二组ID号第二组ID将第一个D组的后缀延伸至后缀,将其与
组合名共享前缀的存储键。
`CreateAdditionalHandleAsync` 仅接受这两个自有的、活集束范围。
处理登记和拆卸所有权的快照 共享一个生命周期锁。
接收的工厂电话,供应商初始化, 终端删除,本地探测器,
和后业主处置
取消呼叫结束
等待; 工厂和供应商初始化保持原版
处置或历史退休后完成的购置
在报告寿命错误之前处置其返回的所有人。
在删除范围或处置共有所有人之前,先对这些被承认的经营业务进行处理。
明确初始化另一个控件。 `RunAsync` 初始化,
执行执行, 并保存主失败时滴泪。 清理删除
仅使用固定方的聚集分割区,并尝试将每个所有者都置于实际
删除操作完成 。 清理调度从调用线索中脱落, 这样可以
同步阻止回击也尊重呼叫者的30秒等待时间。
保留业主并共同承担清理任务,直至作业完成;
`DisposeAsync` 呼叫等待同一任务。超时的例外数据包含
`ClusteringTestKit.CleanupCompletion`,并附上延迟清理故障作为
`ClusteringTestKit.CleanupFailure`. 从未完成的操作保留
其拥有者; 约束适用于打电话者的等待, 而资源释放等待
操作完成后,删除
工程范围已退役;拆卸后直接处置其所有人。
其手柄可能已经
。以后的历史使用与新业主的新固定方式。
普通会员业务收到其假想取消的标志。
处理器释放
依据实际SDK的 关闭、中止或排水量
合同; 由此造成的任何基础设施故障仍然可以观察到。
使用非机密供应商标签:诊断包括标签、群集识别码、不透明
标记、 身份和不匹配的字段都存在。

<a id="independently-discoverable-direct-guarantees"></a>

## 独立发现的直接担保

将每条选选猎方法作为单独的事实/测试,在您的框架框架内进行。
`Task` 并取 `CancellationToken cancellationToken = default`。
行为主张相同。

| ID 身份 | 公共方法 |
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

套件揭露了26个直接案件 加上产生一致

产生的事实通常被命名为
`MembershipTable_ModelBased_GeneratedConformance`;它调用
`MembershipTableModelBasedTestRunner.RunGeneratedConformanceTests`.
产生的案件等待固定的启动和通过协议人处理
单体生命周期钩,每个案件都有新的固定装置和孤立的外圈。
公共选手执行完整生成套件。 主机仓库
系统目标测试在四个 xUnit 病例中分发同样的种子-17清单
保存每一段历史和作业记录。
生成的模型包括18种操作类型。
选择条目 `ReadAllAsync` 使用 `TryGet`.

<a id="comparison-and-protocol-rules"></a>

## 比较和礼宾规则

所需历史从正常 silo 登记开始:新成员
条目有空的怀疑历史。这些历史确认和更改
现有行的卡通式更新,通过投票投票。
保留启动程序的同时,先更新变更状态和怀疑投票票
身份、端点、主机/silo 名称、启动时间和部署元数据。

永久的观察捕捉了所有公共持续进入的入口区域,并筑巢
身份/时间、全部终点/产生身份、排排ETags和
提供方观测结果比较了金刚田和
表格版本/ETag,独立于原始 IAmAliveTime 和供应商定义的行
目标 。 残余物体分遣队也比较了被捕获物体的活性
和行元数据。在相同版本中,历史要求每个非死
和每一保留金银场,同时允许先前的死列
被修剪。 IAmAliveTime 独立且不连贯更新; 行ETags
提供商元数据不透明,可以随心跳或物理重写而变化。
无效/无嫌疑名单和嫌疑人/行查点令
象对一个没有选任角色名称的无效/空表示一样,是可选角色名称。
可选 Azure 部署元数据明确初始化为
空角色/零区,但所有这些字段都保留在每次比较中。

每个成功的协议书都提供真实的当前 `TableVersion.Next()`
并且必须一举一动。ETags是透明的。
有效的下个版本整数和远端状态转换。
以原子方式验证表ETag和替换现有
。在行中,一个提供者可以另外验证或忽略
当表格版本 CAS 保护更新时提供行 ETag , 则该行的 ETag 将保护更新。
连续连续连续每行守卫对只心跳活动仍感到满意。
在相互竞争的横行承诺和原始后使用新读取的行元数据
同一行承诺后 papson 输入。 每个不变的金刚进入字段
。全行写作可以用较早的值覆盖心跳。
不同的阅读可以跳过已指定版本。
确定自己最终死亡,即使观察员
历史保留了最终身份
横压。

只能死一死清理可以保存版本和表格ETag或使用原子+1
套件可接受
批量并约束总版本在零和
删除行。每个保留金刚石场保持不变。
空的清理保留了运河观测和表格版本/ETag。
清理方案保留了死囚排 正好在截断点
截断为不切除, 然后以一个勾前进它并要求删除那行 。
设置在转换前通过所有者通过活体心跳发布
目标行到死,提供与过渡相同的时间戳。
资格使用受管制出版物和开票/投票的卡尼科斯语
接受时标, 接受慢于原始心跳观察。
在同一端点使用严格新一代的再生制。
模型选择合法的远期生命周期操作。

自行删除组别删除后,该组别的数据将被删除,并保留其他组别。
删除结束存储的历史。 G26 校验本地删除和另一个
G27集团使用一个有人居住的外国集群,核查
整个集组的完整视图。
对于外国集束号识别码,范围扩大的提供者可以使这一范围保持不变或
拒绝它, 并用 `ArgumentException` 命名 `clusterId`。 套件校验完成
拒绝后保存的卡尼古力状态;可以删除
如果保留外国范围,G27将核查其
土著探测器建立
范围仍然可读; 退役的把手只接收所有者的处置。

每个拥有 silo 的单位使用单盲栏来公布自己的生存能力。
心跳输入带有身份和时间戳。
整个世界时时时时时时时时时时时时时时时时时时时时时时时时时时时时时时时时时时时时时时时时时时时时时时时时时
每个呼叫保护区的州会会员区和表格版本/ETag。
返回的行元数据可以不同。读取会滞后或暴露旧的生命价值。
生成心跳操作
使用固定所有人按身份处理的固定所有人,并留在该行的寿命范围内。

供应国-当地检验核实每期定期的储存业务预算
心跳: **零前提条件读数,零比较和擦拭操作, 1
盲光活性写**。工具包的观察读到供应商呼叫的括号
检查结果字段; 本地 SDK 仪表设置确定操作
。运行时快照合并保留了最大范围观察到的活性。
供应商-本地测试还验证,在
仅心跳活动需要一次突变尝试,没有心动驱动
隐藏在提供者 API背后的重新读取或重试。通用设想方案设定
边界 API 的结果;SDK仪器确定尝试数。

G07和G08捕捉到ETag行和表格版本, 执行所有者心跳,
然后使用原始输入进行状态或可疑选票变更。
一次成功, 精确的+1 版本预发。 这些案例会让心跳
宽度写字边界上可见的不干涉。
可能堵塞了新存储的心跳;模型自有公布的时印
描述生成的投入,而不考虑返回的原始活性值。

成功前方更新更新, 刷新现有嫌疑人的时间戳并清晰
使用空名单或空名单的人口嫌疑人名单。
缺失行查包括死行后延迟更新
压缩,检查旧牌和新鲜表格候选人。
拥有者在行内行中完成心跳; 本地的失踪行
错误和基础设施例外情形会传播。

货币用途 即时/启动/完成门 准确的赢家计数
并立即对历史前/后已知情况进行观察检查。
ReadAll 假想让读者与前方更新和死组织竞争
清理,包括从现有一行向快照中缺勤过渡。
清理方案情景检查一等能力通过两个把手和接受的清理
提供方的试验涵盖的是原生试验或一种原生增加。
重叠的清理尝试及其争论/行为;运行时间
清理剂日志失效,以后尝试时间。
`concurrencyRowCount` (违约128,
范围3至1000之间,将多行工作量限制在一定范围内,不改变任何说法。
适配器必须选择一个安全计数, 以交叉其后端的实际调用
保留特定供应商的弹进测试,以
明确划定了这些边界。
设置使用初始空全抓图, 然后用一个 `InsertRowAsync` 调用
单 `ReadAllAsync` 每个行的核查结果
下插入和检查插入的行的金字栏
对照独立构建输入。
与所有独立预期的卡门条目相对。

N行是N布尔返回插入,N+3全读,
N(N+1)/2 + 2N 返回会籍行。 N= 4096, 设置返回 8 398 848 行;
N=1001 返回 503 503行。
本地请求、传呼和再录费用。

相连接者使用过渡覆盖生成和执行操作序列。
要求的限定前缀在横行和同行后到达简略表格快照,
心跳后原始的更新, 和一流和多行的 死压紧紧紧紧紧随其后
每个产生的案件都有新的范围;
执行上下文将真实的象征历史与抽象
模式 `DeleteCluster` 是终端:其结果含有经核实的删除证据
并结束所有历史操作。 下一个案例将构建一个新的固定
生成清理使用比每次生成的都晚一个截断点
时间戳,使预期的死row清除与心跳滞后无关
清理结果经过范围检查,并检查其尚存的金字塔行
在模型记录版本 delta 之前验证。 最后执行检查
需要全部十八种操作类型。 自测试两次都进行清理
具有独立神器的战略,可进行独立的卡门式行检;
表格转换衍生行令牌和带有仅用表格的CAS的物理行元数据。
包括交替的活性落后,全流心跳覆盖,便宜
心跳保持,蓄意的卡尼/可口的侵犯
任何固定断裂失败都会阻止生成的运行, 而不是在另一个或其它情况下
重播可以获取所有者。 原案例失败和撕裂诊断
继续提供。
