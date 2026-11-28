# Microsoft.Orleans.Reminders.TestKit

用于验证 Orleans `IReminderTable` 实现及其与提醒器服务集成的测试基础设施。

所有提供程序执行同一份契约。提供程序测试套件只负责构造及配置外部服务；
它们不禁用保证，也不调整重试策略。TestKit 负责确定性数据生成、断言、串行化的
记录数量初始化与清理、有限重试、诊断和失败消息。

<a id="contents"></a>

## 内容

| 类型 | 用途 |
| --- | --- |
| `ReminderTableTestRunner` | 针对每项文档规定的表保证进行直接符合性测试。 |
| `ReminderServiceTestRunner` | 集群级注册、替换、查找、枚举和删除的契约符合性测试。 |
| `ReminderServiceLifecycleTestRunner` | 确定性启动、所有权、精确到期、协调、集群成员变动和清理隔离的契约符合性测试。 |
| `IReminderServiceLifecycleHarness` | 针对单个集群、单个提醒器时钟、诊断和显式拓扑屏障的适配器契约。 |
| `ReminderTableModelBasedTestRunner` | 根据同一完整契约生成并顺序执行的符合性测试。 |
| `IdealizedReminderTable` | 确定性的强一致性参考实现和故障注入判定基准。 |
| `ReminderTableTestFixture` | 用于部署和解析提供程序的进程内集群测试夹具。 |
| `ReminderConformanceException` / `ReminderFailureReport` | 统一的、包含提供程序名称的诊断信息。 |

<a id="contract"></a>

## 契约

共享测试套件要求：

- 幂等启动以及停止后能够持久恢复的重新启动；
- 每次成功插入或更新均由提供程序签发非空 ETag，每次替换均产生不同的 ETag；
- 精确的单点、Grain 和范围读取，包括标识、UTC 整秒精度的 `StartAt`、`Period`、ETag、记录数量，
  以及拒绝重复记录；
- 无符号 `(begin, end]` 环语义，包括回绕和表示整个环的 `(0, 0]`；
- 使用当前和过期 ETag 进行条件删除、准确观察记录不存在，以及 `TestOnlyClearTable`；
- 使用统一的有限变更重试策略，并发替换同一标识和并行执行各 Grain 写入；以及
- 统一的有限读取收敛策略（10 秒，间隔 100 毫秒）。

重试时间从首次尝试之前开始计算，因此即使首次操作阻塞，也受到时间限制。超时诊断保留
提供程序、保证、操作、预期结果、尝试次数、最后一次已完成的观察及最后一个异常。
每个提供程序涉及精确记录数量的初始化及清理均串行执行；并发由专门的测试用例验证。

提供程序测试套件负责其他后端语义，例如基于 `ReminderEntry.ETag` 的条件插入或更新、
启动取消时机、跨表隔离、分页及续页行为。

<a id="adopting-the-suite"></a>

## 接入测试套件

从提供程序测试项目私有引用 TestKit：

```xml
<ProjectReference
    Include="$(SourceRoot)src\Orleans.Reminders.TestKit\Orleans.Reminders.TestKit.csproj"
    PrivateAssets="all" />
```

<a id="create-a-provider-fixture"></a>

### 创建提供程序测试夹具

派生自 `ReminderTableTestFixture`，在进程内 Silo 中配置提供程序。夹具启动表，
公开已部署的 `IGrainFactory` 和 `IReminderTable`，并在套件完成后停止和释放集群。

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

`CheckPreconditionsOrThrow` 在夹具初始化期间捕获外部服务不可用的失败。
在测试构造函数中调用 `EnsurePreconditionsMet`，让测试框架以一致方式报告捕获的结果。

<a id="run-the-direct-contract"></a>

### 运行直接契约测试

派生自 `ReminderTableTestRunner`，传入已解析的表和稳定的提供程序名称，然后通过所用测试框架的特性
公开各项共享保证：

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

内置提供程序测试套件通过 `ReminderTableTestsBase` 继承这些测试。独立套件公开
`ReminderTableTestRunner` 上的每项公共保证，以便在评审时发现共享契约新增的内容。

<a id="run-the-model-based-contract"></a>

### 运行基于模型的契约测试

模型运行器生成确定性的操作序列，覆盖插入或更新、单点与 Grain 读取、无符号范围
读取、当前及过期 ETag 删除，以及清空表：

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

`Seed` 和 `KeyPrefix` 使标识及失败轨迹可重现。运行器在每个生成用例前后清空表，
并在返回前最后确认表为空。

<a id="run-the-grain-facing-reminder-service-contract"></a>

### 运行面向 Grain 的提醒器服务契约测试

`ReminderServiceTestRunner` 通过公共 Grain API 验证注册、查找、枚举、调度替换、ETag 替换及
注销。传入 Silo 为
`ReminderTimeProviderNames.Reminders` 注册的同一个 `TimeProvider`，使到期时间断言使用子系统的时钟：

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

### 运行生命周期与集群成员变动的契约符合性测试

`ReminderServiceLifecycleTestRunner` 补充共享服务级契约。每个服务提供程序都运行同样的八种场景：
启动就绪、单一注册所有权、就地更新调度、删除后的静默、
精确到期恢复、过期所有者注册协调、单个 Silo 加入/离开时的移交，以及清理隔离。

使用 `ReminderTestClock` 作为唯一时间驱动器，并使用 `ReminderDiagnosticObserver` 作为生命周期及触发事件的来源。
适用于 `InProcessTestCluster` 的 `ReminderServiceLifecycleHarness` 适配器提供显式成员资格和提醒器范围
协调屏障。

加入场景使用 Silo 自身的地址哈希启动该 Silo，并等待拓扑协调，然后在该 Silo 的实际所有权范围内选择
确定性的 Grain 标识。标识选择有界，如果找不到匹配候选项，会报告结构化的
契约符合性失败；场景清理仍会删除已加入的 Silo。

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

不要用延迟、重试循环、更长超时或提供程序特定的跳过逻辑替代测试框架屏障。场景清理
使用自己的有限取消令牌，只使用当前 ETag 删除确定性的场景记录，显式推进一次
刷新以等待所有者静默，并验证这些记录不存在；它从不清除无关的提供程序记录，也不会替换
原始场景失败。所有权断言统计本地提醒器实例标识，包括
单个 Silo 上的重复实例，而不是统计不同的 Silo 地址。

内置内存、Azure Table、Cosmos DB、ADO.NET SQL Server、PostgreSQL、MySQL、Redis、DynamoDB 和 Firestore
提供程序均公开相同的继承测试。其适配器仅包含后端先决条件检查、初始化及提供程序
注册。外部服务可用性检查可以使夹具构造被跳过，但没有任何提供程序禁用单项
生命周期保证。未来若某个提供程序无法托管
Orleans 提醒器服务或参与进程内 Silo 成员变动，则在将其排除之前，应在此记录能力边界。

<a id="deterministic-oracle-and-cluster-testing"></a>

## 确定性判定基准与集群测试

`IdealizedReminderTable` 为 TestKit 自身测试和运行时集成测试提供强一致性的参考实现。
`UseIdealizedReminderTable` 在所有 Silo 中注册同一个共享判定基准，并可安装
专用提醒器时钟和提醒器选项：

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

判定基准公开：

- `Snapshot`、`Find`、`Operations` 和 `OperationCount`，用于确定性状态和操作断言；
- `BlockNext`，用于围绕选定的表操作设置阶段屏障；
- `InjectFailure` 和 `SetAvailable`，用于瞬态故障和服务中断场景；
- `FreezeReads`，用于过期读取收敛场景；以及
- 生命周期取消与不变量检查。

`ReminderTestClock` 在构建集群之前创建其生命周期观察者，使启动符合性测试能够在活性及范围协调屏障之前，
等待每个 Silo 的一个 `ReminderServiceStarted` 事件。TestKit
集群集成套件使用这些控制手段覆盖精确到期投递、精确到期存储恢复、
超过平台定时器限制的到期时间、注销后的过期刷新抑制，以及
多 Silo 集群中的单所有者投递。

<a id="diagnostics-and-cleanup"></a>

## 诊断与清理

每次契约符合性失败都会抛出带 `ReminderFailureReport` 的 `ReminderConformanceException`。报告标识
提供程序、保证、操作、提醒器标识、预期与观察到的状态、ETag 演变关系、范围所有权、重试
次数，以及最后的观察结果或异常。

直接测试使用唯一且确定性的标识，并删除其记录。涉及精确记录数量的初始化与清理均
串行执行。基于模型的用例在每个边界清空表。`ReminderTableTestFixture` 负责关闭集群并
释放资源，包括部署失败后的清理。

除共享契约外，提供程序特定测试还覆盖架构、配置、序列化、迁移、模拟器及外部服务
行为。

<a id="consumption"></a>

## 使用方式

提供程序测试项目通过私有项目引用直接使用此项目源码，与
`Orleans.Persistence.TestKit` 的方式一致。
