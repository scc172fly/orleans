# Microsoft.Orleans.Reminders.TestKit

验证 Orleans `IReminderTable` 执行及其提醒服务整合的测试基础设施。

每个供应商都执行一份合同。
它们不禁用保障或调制重试。 TestKit 确定数据、断言和序列
设置和清理、受约束的重整、诊断和故障信息。

<a id="contents"></a>

## 目录 目录 目录 目录 目录 目录 目录 目录 目录 目录 目录 目录 目录 目录 目录 目录 目录 目录 目录 目录

| 类型类型 | 目的目的宗旨 |
| --- | --- |
| `ReminderTableTestRunner` | 每个有记录的表格保证书的直接符合事实。 |
| `ReminderServiceTestRunner` | 聚类级登记、替换、查找、查点和清除符合规定。 |
| `ReminderServiceLifecycleTestRunner` | 确定性的启动、所有权、准确到期、和解、杂乱无章和清洁隔离。 |
| `IReminderServiceLifecycleHarness` | 一组的适应器合同、一个提醒钟、诊断和明确的地形障碍。 |
| `ReminderTableModelBasedTestRunner` | 与同一合同完全一致,产生相继符合的情况。 |
| `IdealizedReminderTable` | 确定性、强烈一致的参考实施和错误注射手肘。 |
| `ReminderTableTestFixture` | 部署和解决供应商问题的流程中集束固定装置。 |
| `ReminderConformanceException` / `ReminderFailureReport` | 统一、供应商名诊断。 |

<a id="contract"></a>

## 合同合同合同合同

共享套房要求:

- 极强的启动和停止后持久恢复;
- a 供每个成功上岗者使用的非空、供应商签发的ETag,供每个替代者使用的不同ETag;
- 准确点、 grain和范围如下,包括身份,统合时秒 `StartAt`、 `Period`、ETag、主要内容,
  重复拒绝;
- (a) 不带符号 `(begin, end]` 环词义,包括整环包装和 `(0, 0]` ;
- 现职和已停工的ETag有条件驱逐、确切缺勤观察和 `TestOnlyClearTable`;
- 使用一个连接的突变回录法政策撰写同身份替换和平行的每 -grain ;
- 一条约束的阅读一致政策(10秒,100毫秒延迟)。

第一次尝试前测量重试时间, 从而将被阻隔的初始操作捆绑起来。 超时诊断仍然保留
提供方、担保、运行、预期结果、尝试计算、最后完成的观察和最后例外。
每一个供应商都对确切的心肌梗塞设置和清理进行序列记录;共同货币由专门的事实来行使。

供应商套件拥有额外的后端语义学,例如 `ReminderEntry.ETag`的有条件调高器等,
启动取消时间、交叉隔离、传呼和持续行为。

<a id="adopting-the-suite"></a>

## 采用套套套件

供应商测试项目私下提及 TestKit :

```xml
<ProjectReference
    Include="$(SourceRoot)src\Orleans.Reminders.TestKit\Orleans.Reminders.TestKit.csproj"
    PrivateAssets="all" />
```

<a id="create-a-provider-fixture"></a>

### 创建提供者固定设置

从 `ReminderTableTestFixture` 提取,用于在进程 silo中配置供应商。固定线开始表格
暴露已部署的 `IGrainFactory` 和 `IReminderTable`,并在套件后停止并处置集束。

```csharp
public sealed class MyReminderTableFixture : ReminderTableTestFixture, IAsyncLifetime
{
    protected override void ConfigureSilo(ISiloBuilder siloBuilder)
        => siloBuilder.UseMyReminderProvider(options =>
        {
            options.ConnectionString = TestEnvironment.ConnectionString;
        });

    protected override void CheckPreconditionsOrThrow()
        => TestEnvironment.EnsureBackendIsAvailable();
}
```

`CheckPreconditionsOrThrow` 记录了固定装置启动期间的外部服务可用性失效。
调用测试构建器的 `EnsurePreconditionsMet` ,以便测试框架报告一致地记录结果。

<a id="run-the-direct-contract"></a>

### 运行直接合同

从 `ReminderTableTestRunner`中衍生出,通过已解决的表格和一个稳定的提供者名称,然后暴露每个共享
使用测试框架属性保证:

```csharp
public sealed class MyReminderTableTests
    : ReminderTableTestRunner, IClassFixture<MyReminderTableFixture>
{
    public MyReminderTableTests(MyReminderTableFixture fixture)
        : base(fixture.ReminderTable, "MyProvider")
    {
        fixture.EnsurePreconditionsMet();
    }

    [Fact]
    public override Task ReminderTable_UpsertRow_ReturnsNewNonEmptyETag()
        => base.ReminderTable_UpsertRow_ReturnsNewNonEmptyETag();

    [Fact]
    public override Task ReminderTable_ReadRows_Range_ExcludesBeginAndIncludesEnd()
        => base.ReminderTable_ReadRows_Range_ExcludesBeginAndIncludesEnd();

    [Fact]
    public override Task ReminderTable_ParallelUpserts_AcrossGrains_RemainIsolated()
        => base.ReminderTable_ParallelUpserts_AcrossGrains_RemainIsolated();
}
```

内置提供套房通过 `ReminderTableTestsBase`继承这些事实。
`ReminderTableTestRunner` 上的公共担保,因此在审查过程中可以看到对共享合同的增补。

<a id="run-the-model-based-contract"></a>

### 运行基于示范合同的合同

模型运行器生成确定性操作序列,覆盖上移、点和 grain 读作, 未符号范围
读物,当前和平淡的ETag清除和清理表格:

```csharp
public sealed class MyReminderTableModelTests : IClassFixture<MyReminderTableFixture>
{
    private readonly MyReminderTableFixture fixture;

    public MyReminderTableModelTests(MyReminderTableFixture fixture)
    {
        this.fixture = fixture;
        fixture.EnsurePreconditionsMet();
    }

    [Fact]
    public Task MyProvider_ModelBasedConformance()
    {
        var options = new ReminderTableModelBasedConformanceOptions
        {
            ProviderName = "MyProvider",
            GrainType = "my-provider-reminder-tests",
            KeyPrefix = "model",
            Seed = 42,
            MaxDepth = 3,
            MaxSequenceLength = 3
        };

        return new ReminderTableModelBasedTestRunner(
            fixture.ReminderTable,
            options,
            output: Console.WriteLine)
            .RunGeneratedConformanceTests();
    }
}
```

`Seed` 和 `KeyPrefix` 使身份和故障痕迹可以复制。
每个生成的立案, 在返回前进行最后空表观测 。

<a id="run-the-grain-facing-reminder-service-contract"></a>

### 运行 grain- 附加提醒服务合同

`ReminderServiceTestRunner` 核实登记、查查、查查、查点、时间表替换、ETag替换和
未经公众登记 grain API相同的 `TimeProvider` 和 silo 登记册登记
`ReminderTimeProviderNames.Reminders` 所以,适时的主张使用子系统时钟:

```csharp
public sealed class MyReminderServiceTests
    : ReminderServiceTestRunner, IClassFixture<MyReminderTableFixture>
{
    public MyReminderServiceTests(MyReminderTableFixture fixture)
        : base(
            fixture.GrainFactory,
            fixture.ReminderTable,
            "MyProvider",
            seed: 42,
            reminderTimeProvider: TimeProvider.System)
    {
        fixture.EnsurePreconditionsMet();
    }

    [Fact]
    public override Task ReminderService_RegisterLookupEnumerateAndUnregister()
        => base.ReminderService_RegisterLookupEnumerateAndUnregister();

    [Fact]
    public override Task ReminderService_UpdateReplacesScheduleAndETagWithoutDuplicate()
        => base.ReminderService_UpdateReplacesScheduleAndETagWithoutDuplicate();
}
```

<a id="run-lifecycle-and-churn-conformance"></a>

### 运行生命周期和周期性

`ReminderServiceLifecycleTestRunner` 添加共享服务级合同。
服务提供商:开办准备、单一登记所有权、现场时间表更新、清除不便、
及时追回、陈旧的所有人登记核对、一silo 连/休假转移以及清理隔离。

使用 `ReminderTestClock` 作为唯一的时间驱动程序,使用 `ReminderDiagnosticObserver` 作为生命周期/ ticklick 源。
`ReminderServiceLifecycleHarness` 用于 `InProcessTestCluster` 提供明确的成员和提醒-
和解障碍。

加入假想开始 silo , 以其自然地址杂乱状态启动, 等待在选择前进行地表调节
确定论 grain 身份 silo身份选择受约束,报告a
找不到匹配的候选人时结构结构不一致性; 情景清理仍然删除了合并 silo。

```csharp
var clock = builder.AddReminderTestClock();
var cluster = builder.Build();
await cluster.DeployAsync();

var options = cluster.Silos[0].ServiceProvider
    .GetRequiredService<IOptions<ReminderOptions>>().Value;
var harness = new ReminderServiceLifecycleHarness(
    cluster,
    clock,
    clock.DiagnosticObserver,
    options.ReminderLoadingWindow);

public sealed class MyLifecycleTests : ReminderServiceLifecycleTestRunner
{
    public MyLifecycleTests(IReminderServiceLifecycleHarness harness)
        : base(harness, "MyProvider", seed: 42)
    {
    }

    [Fact]
    public override Task ReminderService_OneSiloJoinLeaveTransfersOwnership()
        => base.ReminderService_OneSiloJoinLeaveTransfersOwnership();
}
```

不以延迟、 重新运行环、 超时或特定供应商跳过来取代带环屏障。 方案清理
使用自己的约束牌, 使用当前ETags 来去除仅确定性情景的行, 并使用其当前的ETags, 前进一列
用于自控自控器不自密的刷新, 并验证其不存在; 它从不清除无关的提供行或替换
所有权主张计算当地提醒事件身份,包括重复
在一个 silo地址上,而不是计算单独的 silo 地址。

内置的存储器、 Azure 表、宇宙 DB、 ADO.NET SQL 服务器、 PostgreSQL、 MySQL、 Redis、 DynamoDB和消防站
它们的适应器只包含后端先决条件/设置和提供者
提供外部服务可跳过固定建筑,但没有任何供应商能够使个人残疾
寿命周期保障。在此添加一个有文件证明的能力界限,然后忽略无法托管的未来供应商
排 Orleans 提醒服务或参与处理中 silo 胡说八道

<a id="deterministic-oracle-and-cluster-testing"></a>

## 确定性神器和集束测试

`IdealizedReminderTable` 提供 TestKit 自测试和自试的严格一致的参考执行
`UseIdealizedReminderTable` 登记每个 silo 共有的孔器,并安装
专用提醒时钟和提醒选项:

```csharp
var builder = new InProcessTestClusterBuilder();
var reminderClock = TimeProvider.System;
var oracle = builder.UseIdealizedReminderTable(
    reminderTimeProvider: reminderClock,
    configureReminderOptions: options => options.RefreshReminderListPeriod = TimeSpan.FromSeconds(1));
var cluster = builder.Build();
await cluster.DeployAsync();

var records = oracle.Snapshot();
var operations = oracle.Operations;
```

先知揭发:

- `Snapshot`、 `Find`、 `Operations`和 `OperationCount` ,用于确定状态和运行主张;
- 选定表格业务周围的阶段障碍`BlockNext` ;
- 短暂故障和断电情景的`InjectFailure` 和 `SetAvailable` ;
- 用于读取淡化趋同设想情景的`FreezeReads` ;以及
- 取消生命周期和无差别检查。

`ReminderTestClock` 在集群组建立之前设立其生命周期观察者,允许启动合规等待
单 `ReminderServiceStarted` 用于每 silo 和距离调和障碍。 TestKit
集束集成套件利用这些控制手段,处理应交付、应收回储存、应归还的准确交货、应归还的仓储、
超过平台定时时限的到期时间,未登记后停止再登记,以及单人交货
-silo 组合体。

<a id="diagnostics-and-cleanup"></a>

## 诊断和清理

每次不合规都丢弃 `ReminderConformanceException` ,并丢出 `ReminderFailureReport`。
提供方、担保、操作、提醒身份、预期和观察状态、ETATag线系、牧场所有权、再加工
以及最后的观察或例外

直接事实使用独特的确定身份,并删除行排。
以模型为基础的案例清除了每个边界的表格。 `ReminderTableTestFixture` 拥有集束关闭和
资源处置,包括部署失败后清理。

具体供应商的测试包括:计划、配置、序列化、迁移、模拟和外部服务
与共享合同同时发生的行为。

<a id="consumption"></a>

## 消费消费

供应商测试项目利用私人项目参考书,从源头消耗项目,匹配
`Orleans.Persistence.TestKit`.
