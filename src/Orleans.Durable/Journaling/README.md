<a id="microsoft-orleans-journaling"></a>

# Microsoft Orleans 日刊

<a id="introduction"></a>

## 简介
Microsoft Orleans 日刊按订购的期刊数据,维持了持久性状态变化,可重播这些数据以收回模拟的耐久收藏和价值。

包包包括 JSON 线基储存格式 System.Text.Json 与一个日记存储供应商如 Microsoft.Orleans.Durable.AzureStorage储存提供者仍然独立于序列格式: Microsoft.Orleans.Durable 提供日记格式和关键耐用输入编码,这些是耐用的国家组成部分用于编码和恢复自己的业务。

应用代码使用 `IDurableStateManager` 管理 grain的耐久状态,声明其组成部分和确认写作。
`IStateMachine` 界定了国家执行的低级重播和快照程序。
`IJournaledStateManager` 是一份独立合同,涉及日记本所有权和终身业务。

<a id="volatile-storage-snapshot-thresholds"></a>

## 易挥挥挥散储存快快的阈值

`AddVolatileJournalStorage(options => ...)` 配置
`VolatileJournalStorageOptions.MaxAppendsBeforeSnapshot` (违约100)和
`MaxBytesBeforeSnapshot` (违约 1,048,576 字节)。
每个提供者独立配置。 两个值必须是正数 。
供应商在构建时捕捉配置的限制 。

达到这两个门槛都需要在下一期日记写前提供快照。
成功替换替换重置两个对控台;快照本身不包含
将历史从附加历史中重新设置。 删除和娱乐也重置历史。 单
附加件可超过字节阈值。
超过更多更新; 小限制会减少保留的历史和重播工作。
对应器属于每一份日记账簿,由同一日记账的手柄共享
在一个供应商内部。
保存的内存包括最新的快照,并附有编码字节,直至和
包括过境附件和提供商簿记。

<a id="getting-started"></a>

## 入门
要使用此软件包, 请通过 NuGet安装 :

```shell
dotnet add package Microsoft.Orleans.Durable --prerelease
```

<a id="example---configuring-json-journaling"></a>

## 示例 - 配置 JSON 日记
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;
using Orleans.Durable.Journaling;
using Orleans.Durable.Journaling.Json;

var builder = Host.CreateApplicationBuilder(args)
    .UseOrleans(siloBuilder =>
    {
        siloBuilder
            .UseLocalhostClustering()
            .AddAzureBlobJournalStorage()
            .UseJsonJournalFormat(JournalJsonContext.Default);
    });

using var host = builder.Build();
var cancellationToken = host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping;
await host.RunAsync(cancellationToken);

[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(ulong))]
internal partial class JournalJsonContext : JsonSerializerContext;
```

如果您需要自定义 `JsonSerializerOptions`, 请通过选项管道配置 `JsonJournalOptions` :

```csharp
siloBuilder
    .AddAzureBlobJournalStorage()
    .Configure<JsonJournalOptions>(options =>
    {
        options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.AddTypeInfoResolver(JournalJsonContext.Default);
    });
```

供应商登记电话 `JournalingHostingExtensions.AddJournaling` 登记核心服务,
使用 `AddJournalStorage<TProvider>`
用于应用程序供应的存储供应商。 `AddJournaling` 是核心设置;供应商登记
提供后盾储存。

将 `[JournalStorageProvider("orders")]` 应用到一个 grain 类,以选择命名
供应商注册使用,例如 `AddAzureBlobJournalStorage("orders", options => ...)`。
Grain 类型类型,没有明确的选择用途 `Default`. `DurableGrain` 助者,
注射耐久值和收集,以及日记支持的持久性持续状态
共享激活的管理员和选定存储。注册相同的名称
每一个 silo 主机主机 grain 类型。

回收使用现有
更改现有期刊中的 grain 类型选择
需要一项有意的数据迁移或削减战略,包括倒退。

JSON 线是默认的 `JournaledStateManagerOptions.JournalFormatKey`存储提供者通过 `IJournalMetadata.FormatKey` 和 `JournalMetadata.FormatKey`恢复期间, Orleans 用于选择匹配的日记格式和持久操作编码的存储密钥。 如果非空白日记没有存储的格式元数据, Orleans 将它视为遗产 OrleansBinary 兼容性数据。

如果您已经用 OrleansBinary 格式写入数据,可以在计划迁移时继续使用:

```csharp
siloBuilder
    .AddAzureBlobJournalStorage()
    .ConfigureServices(services =>
        services.Configure<JournaledStateManagerOptions>(options =>
            options.JournalFormatKey = "orleans-binary"));
```

移徙至 JSON配置 `JournaledStateManagerOptions.JournalFormatKey` 至 `JsonLinesJournalFormat.JournalFormatKey` 调和 `UseJsonJournalFormat(...)`提供,由 `JsonJournalHostingExtensions`. When a grain 回收与配置的写入格式不同的数据, 下一个写入被强制进行完整快照, 以便日记使用 JSON 并更新存储格式元数据。

<a id="example---using-durable-state"></a>

## 实例 - 使用耐久状态
```csharp
using Orleans;
using Orleans.Durable.Journaling;

public interface IShoppingCartGrain : IGrainWithStringKey
{
    ValueTask AddItem(string itemId, int quantity, CancellationToken cancellationToken);
    ValueTask<Dictionary<string, int>> GetItems(CancellationToken cancellationToken);
}

public sealed class ShoppingCartGrain(IDurableStateManager stateManager)
    : Grain, IShoppingCartGrain
{
    private readonly IDurableDictionary<string, int> _cart =
        stateManager.GetOrAddDictionary<string, int>("cart");

    public async ValueTask AddItem(string itemId, int quantity, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _cart[itemId] = quantity;
        await stateManager.WriteStateAsync(cancellationToken);
    }

    public ValueTask<Dictionary<string, int>> GetItems(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new(_cart.ToDictionary());
    }
}
```

字段初始化器在施工期间宣布 grain状态的车件组件。
`AddJournaling` 注册后, grain 与激活装置一起建造的寿命周期
`IGrainContext`,在解析返回之前。 `SetupState` 的恢复工作在 `OnActivateAsync`之前完成
和 grain 普通要求 `Grain` 应用程序拥有的子类 grain 基数, `IGrainBase`
执行,以及启动范围特征可以输入 `IDurableStateManager`。
存储服务提供; 只有解决管理员的激活方能执行每 -grain
日记I/O

`DurableGrain` 仍是一个便利基级基本等级,通过
`StateManager` 和转交其受保护的 `WriteStateAsync` 助手。 Grain-- -- 范围广泛的管理人员被录用
解析返回之前的分辨率返回。自定义管理器替换者在其构建者或
注册工厂、注册 `ILifecycleParticipant<IGrainLifecycle>` 或签署
直接启动和关闭回调。

默认 grain 管理器既执行独立合同,也执行 `IDurableStateManager` 和
`IJournaledStateManager`,在同一激活范围对象上。自定义 grain 管理器替换
合同和登记均酌情提供。

共同启动启动安装操作在施工后同步运行,并可以首先解决经理问题
管理人员通过现有的生命周期参与机制订阅。
决议 grain-- -- 施工或同步启动期间,所有经理人和宣布国家部件
设置,在启动生命周期之前就建立用户订阅。如果使用周期的订阅失败,
建造者处置失败的经理及其作家,然后宣传失败。
a. 使用回收状态的特性在 `SetupState`之后运行;
程序激活前需要工作才能启动 `Activate`。 运行调用机的取消标记
以非同步 grain 和州管理操作方式进行。

`GetOrAddState<TState>(name)` 接受申请合同,例如
`IDurableDictionary<string, int>`,并使用注册工厂建造其国营机器
`DurableStateManagerExtensions` 用品 `GetOrAddDictionary`、 `GetOrAddList`、
`GetOrAddQueue`, `GetOrAddSet`, `GetOrAddLog`, `GetOrAddValue`, `GetOrAddTaskCompletionSource`,和
`GetOrAddPersistentState` 帮助者。

在启动前,在施工或同步激活装置期间宣布新的状态组件。
稍后 `GetOrAdd` 请求解决现有姓名;请求
替换登记册前一时丢失了名称的错误名失败。 `TryGetState<TState>`
查找而不创建。 在已宣布的耐久字典中使用密钥来显示运行时间变化身份 。

在一个管理人员内部使用名称的正交式比较。
同一实例;不兼容的合同或封闭式通用类型立即失效。
`[FromKeyedServices("cart")] IDurableDictionary<string, int>` 返回与
`stateManager.GetOrAddDictionary<string, int>("cart")`,不管哪条路先解决它。

内建耐久状态组件自动使用配置的 JSON 编码器。 配置 `JsonJournalOptions` 以控制输入有效载荷所用的 `JsonSerializerOptions` 实例。 发布命令名称和记录形状由存储格式固定, 因此序列器命名策略只影响用户的有效载荷值 。

用于裁剪和本土AOT,使用 `Configure<JsonJournalOptions>(...)` 配置 `SerializerOptions.TypeInfoResolver`, `SerializerOptions.TypeInfoResolverChain`,或 `JsonJournalOptions.AddTypeInfoResolver(...)` 带有每个日记密钥、值和状态类型的源生成元数据。 `UseJsonJournalFormat(JournalJsonContext.Default)` 指的是推荐的低反射路径,当您也想要启用 JSON 格式。如果元数据不可用,则 JSON 耐久输入编码器失败, 配置错误, 而不是返回到基于反射的序列化 。

<a id="observing-state-and-streaming-drafts"></a>

## 观测状态和流汇草稿

Grains 暴露监视流在其耐久状态上作为 `IAsyncEnumerable<T>` 方法:

```csharp
public sealed class ConversationGrain(IDurableStateManager stateManager) : Grain, IConversationGrain
{
    private readonly IDurableLog<string> _messages = stateManager.GetOrAddLog<string>("messages");
    private readonly IDurableValue<string> _status = stateManager.GetOrAddValue<string>("status");

    public IAsyncEnumerable<LogEvent<string>> WatchAsync(long epoch, long nextSequence, CancellationToken cancellationToken = default) =>
        _messages.ReadAsync(nextSequence, epoch, cancellationToken: cancellationToken);

    public IAsyncEnumerable<ValueChange<string>> WatchStatusAsync(CancellationToken cancellationToken = default) =>
        _status.WatchAsync(cancellationToken: cancellationToken);
}
```

`DurableStateWatchExtensions.WatchAsync` 监视一个耐久值。 观察和日志读取显示变化
日记后写下它被确认, 所以读者只观察耐久状态。
注册的著作刊登了经理每位看门人共享的一个水印。

每一个写作都捕捉每个州, 坚持它所捕捉的东西, 然后承认它。一个变化是一时的改变
正在运行中写入属于下个写入, 所以 `IStateMachine.OnWriteCompleted` 执行并
`IDurableTaskCompletionSource<T>.Task` 仅报告已完成的写入记录。
另一线索则保持一个日记条目打开, 独立管理器的所有人可以将该条目写入等待
关闭并随后用其他每处变化来捕捉它, 所以 `WriteStateAsync` 确认每一个阶段的改变
在那日,被召唤的时候,

价值观察提供最新承诺值,不积压。 管理员创建其观察员
第一次观察开始时状态, 而未监视的状态则没有发布任何信息。 观察需要恢复
。在下一个写入后,将开始一个手表,该手表将随着数值的逐步确定而交付给下一个写作。

`IDurableLog<T>` 是一个仅附加的日志,其序列号耐久、单质增加 `long` 。
`Append` 返回条目序列, `CommittedSequence` 则表示耐久条目的结束。
`TrimBefore` 约束保留, `Clear` 替换条目并消耗一个序列号,所以顺序
数字在历史中从未再利用,且 `Seal` 可持久结束日志。 `Epoch` 确定历史:
日志创建或日记删除后的第一个更改, 以新的随机时代开始历史
以它为日记,读者位置是一个时代和一个序列号,每个 `LogEvent<T>`
轴的, `Epoch`. `ReadAsync(fromSequence, epoch, includeDrafts, resumeDraftId)` 承诺重新阅读
条目从位置条目中获取,等待新条目,因此读者收到的每个条目都是有效的恢复点。在剪裁条目时,每个活动阅读器都接收 `Trimmed`
`Reset` ,当它所观察到的条目被替换时:用一个清晰、删除日记或因为它恢复
任何激活中, 都存在另一个历史时代或清除序列号前后的空序号。 流会传送
`CaughtUp` 之后的一则条目,当读开始和结束时所执行的条目 `Sealed` 封印一旦发出,
具有持久性。

`BeginDraft` 打开一份 `Write` 项目送达读者的草稿, 即立即通过 `includeDrafts: true` 。 `Commit` 附加零
或更多条目,作为一份日记项并报告 `DraftCommitted` ,且承诺范围; `Commit()` ,无
将条目干净地关闭草稿。 `Abandon` 或处置报告 `DraftAbandoned` 具有 a 的
`LogDraftAbandonReason`封闭式草案释放其项目,因此读者没有读过每个项目
收到下一个结果,而从未收到其余结果;承诺的条目含有最终内容。
当日记被删除或密封时, 日志关闭打开草稿, 将它关闭, 检查 `IsOpen` 在承诺
日志可以关闭草稿的草稿。日志日志会将草稿的识别特征及其承诺条目进行日志, 因而是读者
恢复承诺草案的,在任何激活中接收 `DraftCommitted`,而日志保留第一个
被指定条目。 读者如果恢复当前放弃的激活程序得到其理由的草稿, 则会收到它的理由;
记录最近被弃的16份草稿的原因。
启动打开并从未被承诺过 接收 `DraftAbandoned` 与 `PreviousActivation`。

日记写错误失败, 监视流; 被停止的管理器在没有 `Sealed`的情况下完成 。

测试使用耐久状态的代码, grain 在一个 `InProcessTestCluster` 其 silos 调 `AddJournaling()`,
应用应用程序的日记账格式配置,并调用 `AddVolatileJournalStorage()`。 silos 运行
真实的日记国家经理 在模拟杂志上。

<a id="custom-state-and-standalone-ownership"></a>

## 自定义状态和独立所有权

a 登记册 grain- 符合国家组成部分合同及其与
`services.AddStateMachine<TState, TImplementation>()` 日期 时 `IServiceCollection`两种类型的参数
是指参考类型,执行既执行 `TState` 又执行 `IStateMachine`。
在 silo 配置中,使用 `siloBuilder.AddJournaling()` 设置核心元件和
`siloBuilder.Services.AddStateMachine<TState, TImplementation>()` 排排 grain 状态组成部分绘图。
存储供应商登记已在内部调用 `AddJournaling` 。 Grain 代码获得组件
中 `IDurableStateManager.GetOrAddState` 使用现有的激活范围。管理器操作
建筑、登记和期刊-流装订;施工者接收
申请人的无证明申请合同产生
`GetOrAddState`时的清清楚登记错误。

无参数登记超载通过依赖解决建筑商的依附关系
输入。对于名称意识的构造,使用超载接收
`Func<IServiceProvider, string, TImplementation>`:回调接受自控服务
提供方和被要求的国家名称。

`IStateMachine` 包括:

- `Reset`:重置记忆状态并约束一个 `JournalStreamWriter`。
- `ReplayEntry`:从记录的行动中重建状态。
- `PrepareWriteAsync`:为待决状态建立不同步的持久性先决条件。
- `WritePendingEntries`:将待处理的操作发送到提供作者。
- `WriteSnapshot`:向提供作者发送可重建的快照。
- `OnRecoveryCompleted`:在应用使用前完成重建。
- `OnWriteCompleted`:公布取决于储存确认的效果。

命令支持状态可以使用 `IDurableValueCommandCodec<TCommand>` 编码
将它应用到本地时, 命令在全部命令中完成后才能将其应用为本地。
用于重建整个州的国家。
用于 Orleans 序列和供应 System.Text.Json 使用元数据时 JSON 代码码

回收重置国有机器和重放耐用条目。
经理和注册州同属一地。 `JournalReplayContext.ResolveStateMachine` 路径条目的
他们的流体状态机器。

编写工作在附件或快照捕捉之前进行。
每一个州都自行制定自己的先决条件。
准备好的先决条件,同时准备;然后管理人员重新检查所有各州
包括由跨左转所引发的突变。
准备失败保留,等待修改以明确
重新写入并报告 `JournaledStatePreCommitException`。所有者的关闭标记
取消准备工作,处置工作排干实际完成。

`IJournaledStateManager` 独立于 grain- 面 `IDurableStateManager` 并扩展
`IAsyncDisposable`. 其所有人 API 提供 `RegisterStateMachine`、 `TryGetStateMachine`,
`InitializeAsync`、 `WriteStateAsync`、全日 `DeleteStateAsync`和 `PendingWriteByteCount`。
`IJournaledStateManagerFactory.CreateStandalone(JournalId)` 返回该所有人合同。
状态机器组件,有呼叫员提供的附属关系,并在启动前登记。
协助者接受呼叫者拥有的部分和在恢复后更新的操作:

```csharp
using Orleans.Durable.Journaling;

public static class StandaloneJournal
{
    public static async ValueTask Update(
        IJournaledStateManagerFactory factory,
        JournalId journalId,
        IStateMachine component,
        Action updateComponent,
        CancellationToken cancellationToken)
    {
        await using var stateManager = factory.CreateStandalone(journalId);
        stateManager.RegisterStateMachine("count", component);
        await stateManager.InitializeAsync(cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        updateComponent();
        await stateManager.WriteStateAsync(cancellationToken);
    }
}
```

与日记配置的写写格式和保留格式相匹配的调用器供应组件编码器
独立日记业主从不创建或处置
DI范围。如果部件的施工需要工程范围,则呼叫器供应和管理。
所有人停止日记处理和发布日记资源;构成部分和扶养财产处置
`TryGetStateMachine` 寻找一个明确注册的部分。

Grain- 向上 `GetOrAdd` 和按键分辨率使用现有的激活范围。运行时间自有
其设计的国家组成部分、依赖关系和管理人员清理的范围及其寿命。
独立一体化,如 " 持久就业 " 等独立融合,明确提供了自己的国家组成部分
并使用所有者界面协调回收和写作。

明确设立管理人员 `JournalId` 中 `CreateStandalone`,或建造的
直接从储存中直接储存,不使用 grain 背景,保留呼叫者拥有的初始化和处置,即使
内创建 grain 登记其国家组成部分,等待 `InitializeAsync(cancellationToken)` 与
使用前操作的标记,或通过指定注册来故意提供
通过明确的新闻工厂创建的工厂使处理不合格者保持独立
环境 grain 上下文,包括调用者随后将管理人员纳入生命周期。

<a id="state-identity-and-retirement"></a>

## 国家身份和退休

保存所有启动和部署的州名。
自动退休宽限期,在出现7天的违约情况下通过
`JournaledStateManagerOptions.RetirementGracePeriod`. 追回在[质 期间保留其记录
周期。 在以后启动的恢复装置中重新命名;
宽度期间将去除已留存的流。 声明每个激活时必须保持活跃的状态组成部分,
即使内容无法查阅。

应用程序/程序命名更改保留了国家名称、流身份、命令等长期存在的国家名称
符号、 格式键值、 序列成员身份和提供源元元密钥。 保持代码化
在整个部署和回滚窗口中,用于已退役流和存储格式。

<a id="staging-and-failure-boundaries"></a>

## 储存和故障边界

每位使用国家经理的来电者都共享未结日记。
操作中本地数据中确认的数值。一旦结果安全进行,则将其变异适用于
持久状态和等待的状态 `WriteStateAsync`一份确认书涵盖全管理人员期刊
包括由间断电话人进行的改动。 应用程序负责对过渡进行排序
并造成不确定结果的重整

失败的写作或永久删除, 将管理器、 排队的故障操作和请求永久围上
相关 grain的解除。在飞行中的通话保持其现有记忆状态,随后则保持
州管理器操作明显失败。 新的激活可以恢复实际的持久结果 。
对于通过 `IJournaledStateManagerFactory`创建的管理器,处置失败实例并创建
同一 `JournalId`的另一名经理,明确建造和登记新的状态组件
在启动之前,并根据旧部件和依赖性指定寿命将其初始化并退休。

初始化失败时, 向尝试的来电员报告错误, 让经理无法启动 。
再次调用 `InitializeAsync` ,以便使用现有的重设/重放合同从头重试。
并写作在恢复成功后便可以使用。
国家登记在启动之初即停止。

取消呼叫者等待的等候时, 留下一个已经排队的写正在运行。 通过写入观察耐用性
是否重试应用程序命令之前确认或重新激活。

<a id="storage-provider-contract"></a>

## 储存提供商合同

该日记是持久状态的真理来源。 `IJournalStorage` 执行中提供这些保障:

- `AppendAsync` 使得整个部分在早期的每个段段之后都能看到,或者说没有一个,并且 `ReplaceAsync` 将它的全部价值都放在期刊唯一的部分上,或者把日记保留其先前的内容。 储存于多个记录或请求中的一段通过原子出版步骤就能看得见,因此成功读到在提供者内部崩溃、超时、冲突或重试后对整段进行观察。
- 超过提供者限制的段段除例外无法通过整个调用,任何部分都无法看得见。
- 明确拒绝的书写内容留下先前的内容。 含有不确定结果(如请求发出后暂停)的书写应用了整段或其中任何一部分。 重试可以报告原始请求提出后出现的先决条件冲突; 追偿决定结果。
- 成功 `ReadAsync` 返回一个日记版本:每个部分,只要写到该版本,全部和按写序。调用者从失败的读取中丢弃数据并从开始恢复 。
- 每个存储实例都具有由首次成功读作或创建所设定的、包括观察到的缺失在内的书面先决条件,并由其本身的写作加以推进。 `AppendAsync`、 `ReplaceAsync`和 `DeleteAsync` 仅在该版本中成功并以其他方式丢弃 `InconsistentStateException`。一个实例在读到日记或用 `CreateIfNotExistsAsync`创建该日记后写;通过一个既未丢出 `InvalidOperationException`的案例撰写。元数据操作从不确定也不移动该先决条件。

调用员等待每次操作,然后通过同一个存储实例启动另一个操作。 单独的情况通过储存的先决条件协调。 直接储存调用员在写作前先以 `ReadAsync` 或成功 `CreateIfNotExistsAsync` 开始设定其先决条件。

事后 `ReadAsync` 调用确认既定的先决条件,如果另一个作家更改了内容,则丢弃 `InconsistentStateException` 。这提供了历史通过同一界面读取,同时保留活跃作家的前提条件。冲突后恢复使用新的存储实例。

追加文件至少包含一个字节。 空白的附加文件在触摸存储前会丢弃 `ArgumentOutOfRangeException` ; `ReplaceAsync` 接受空值以发布空日记 。

供应商符合规定的套房(`JournalStorageConformanceTests` 内 `test/Orleans.Journaling.Tests/Conformance`以限制、通货、错误注射和基于模型的测试来核实这些担保。本存储处的每个供应商都运行该软件库。

<a id="storage-format"></a>

## 储存格式

JSON 将日记条目格式化为真实的 JSON 行: UTF-8 文本,无字节顺序标记,每个日记条目行有一个 JSON 阵列。每行都由 `\n`终止。回收既接受LF和CRLF行结尾。使用格式元数据的存储供应商应存储 `JsonLinesJournalFormat.JournalFormatKey` 作为格式键,并可以使用 `application/jsonl` 作为MIME类型。

每个记录中包含作为元素0的状态代号,持久运行有效载荷阵列作为元素1:

```json
[8,["set","alpha",1]]
```

在操作有效载荷阵列中,元素0是命令名称,后面是命令专用的魔方,如键、值、项目阵列或版本。存储写入批次附加一个或多个完整的 JSON 线记录,但不增加一个单独的范围信封或最后的容器关闭步骤。

现有数据是使用其存储的格式元数据阅读的,或者在没有元数据时作为遗留 OrleansBinary 数据阅读,然后通过下个快照写入迁移到配置的写入格式。

<a id="catalog-enumeration"></a>

## 分类表列举

`IJournalStorageCatalog.ListAsync` 返回提供者跨行订单中的 `IAsyncEnumerable<JournalCatalogEntry>` 。
每个条目中包含 `Id` 和任择 `Metadata`复制的 `Id` 当身份特性与
需要,因为重复输入可以包含不同的元数据版本。
`JournalCatalogListOptions.Prefix` 符合 `JournalId.Value`的原始起始点,包括部分路径
例如, `jobs/shards/20260909` 为UTC日选择了时间戳名称。
使用尾部斜线, 如 `jobs/shards/`来选择命名空间的子孙 。
`MinId` 和 `MaxId` 供应中包含下限和上限值的供给。
`StringComparison.Ordinal` ,当开始计数时被快速抓取。默认值
允许相应的限制被打开。不连接的限制产生空结果。

```csharp
var options = new JournalCatalogListOptions
{
    Prefix = new JournalId("jobs/shards/20260909"),
    MinId = new JournalId("jobs/shards/20260909T1000000000000Z-"),
    MaxId = new JournalId("jobs/shards/20260909T1200000000000Z~"),
    IncludeMetadata = true
};
```

供应商可以使用上下共同的前缀进一步缩小本地的前缀
。需要统一结果顺序的应用程序对选定的ID进行排序
`StringComparer.Ordinal`.

存储提供者内部取取页面,并随着发现这些页面时生成匹配的身份。 `await foreach` 预发跨行并在循环结束时处置该查点器。分批处理身份的消费者可以保留一个逐批进行查点的统计器,并按序在最后待决 `MoveNextAsync` 完成后予以处理。使用其一生都包含此查点内容的取消标记。

`IncludeMetadata` 默认为假的, 并用范围选项快速描述。 如果是, 则提供方
清单中列出的完整元数据:日记格式、储存ETag和呼叫者拥有
使用与 `GetMetadataAsync`. Azure 布罗布项目相同的语义,同时观测到的特性。
WAL 元数据、 Azure 表工程头属性和挥动快照存储下的元数据
锁定 S3 和 Redis 返回无效元数据,因为其列表显示身份而不是
完整日记元数据。预测没有增加每期日刊的单独元数据请求。

需要元数据的消费者使用提供的快照,或在缺少时拨打 `GetMetadataAsync` 。
空的调用器- 财产字典是有效的完整快照。 请将ETag 当作该版本
将快照转至有条件元数据更新; 同时变动可能导致更新失败 。

| 提供商 | 如何狭小的发现扫描 | 剩余剩余工作 |
| --- | --- | --- |
| 挥变 | 命令键索引选择一个视图,覆盖要求的前缀和边框。 | 抓图选中的密钥并检查当前日记的存在 。 |
| Azure 表格,默认绘图 | 可打印 ASCII id 使用两个大写字母的每个字节的十六进制数字, 允许直接索引前缀和下/上键过滤器。 | 查询包括日记页眉行条件;服务控制在选定关键区域内工作。 |
| Azure 表格,自定义绘图 | 卡尼西亚日记牌过滤器限制返回页眉。 | 任意制图可能需要扫描表,因为日记标识属性没有索引。 |
| Azure 组合 | `wal/` 命名空间和原始id前缀选择 WAL 边框;保守的 ASCII 在平坦和等级名称空格中安全地将窄跨行绑定。 | 宽度和最后页面可以包括额外的候选人,根据原正则范围过滤。 检查站使用单独的命名空间 。 |
| S3 普通用途,可按订单列入名单 | `wal/` 命名空间不包括检查站。身份标记键使用本地的前缀和在包容性下下限之前的第一个 `StartAfter` 标记,然后停在上WAL键处。 | 最后页面可以覆盖范围。 自定义密钥映射使用他们配置的本地前缀和身份过滤 。 |
| S3 快式目录桶 | `wal/` 内原本地刀切的预设物限制命名空间,排除检查站。 | 在无顺序的跨行期间,部分名称和时限被过滤。 |
| Redis | 可读关键词名可以在身份元数据读作默认映射之前,先进行本地 `SCAN MATCH` 前缀过滤和本地密钥范围检查。 | `SCAN MATCH` 仍然横穿服务器密钥空间。自定义密钥映射从匹配的元数据 hashes 读取卡尼卡代号 。 |

Blob 本地边框保留了共同列名前缀,并在标点或
命令 S3 本地的低/上优化
储存订单与包括固定宽的ASCII在内的定序身份订单一致
保存时,所有原始界限在跨越储存期间仍然有效。
通用桶`UseOrderedListing` 能力设置。
选择的身份计数、 传输的密钥和后端扫描工作是单独成本 :
服务器侧过滤可减少传输的数据, 而服务仍然在检查一个更大的密钥空间。

数字观察现场存储。 同时变化会跟随每个供应商的列表语义; 呼叫者应该容忍在修改过程中重复身份, 并使用随后的查点来发现以后更新情况。 日记的存在可以在发现和储存操作之间发生变化。 取消和存储错误会通过查点传播。 解开一个失败的点名器, 在重新尝试列表操作时开始新的查点 。

<a id="provider-metrics"></a>

## 提供方衡量量

`Microsoft.Orleans` 仪表记录按行进和明确供应商的重试分类:

- `orleans-journaling-provider-catalog-pages` S3和S3期间收到的 Azure 单表
  跨行,包括空页。
- `orleans-journaling-provider-catalog-items` 计数本地页候选人并消费 Redis
  本地过滤前扫描密钥 。
- `orleans-journaling-provider-catalog-entries` 由S3交付的条目, Azure 布洛布, Azure
  表、 Redis和多变性提供者。
- `orleans-journaling-provider-retries` 计数明确的供应商-循环重置。

页、项目和条目使用提供者提供的 `provider` 标记;重试还使用 `reason`。
每个提供库拥有其简称,例如用于模拟提供者的 `volatile` 。
将候选人与交付的条目进行比较,以评估过滤和重复抑制。

现有的国家管理和具体供应商的计量标准保留储存时间、结果和字节
应用用品 [SDK 客户端遥测](../../docs/site/src/content/docs/grains/journaling/operations.md#use-host-provided-dependency-telemetry)
通过主机集成,如喷射、仪器仪表图书馆或SDK诊断
配置。使用这些依赖时间和结果以及服务方交易的信号
记账调节的衡量标准。

<a id="documentation"></a>

## 文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件文件
关于更全面的文件,请参见:
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Grain 持久性](https://dotnet.github.io/orleans/docs/grains/grain-persistence/)
- [System.Text.Json 文档](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/overview)

<a id="feedback--contributing"></a>

## 反馈和贡献
- 如果有任何问题或希望提供反馈,请 [GitHub上的开关](https://github.com/dotnet/orleans/issues)
- 加入我们社区 [不一致](https://aka.ms/orleans-discord)
- 跟随 [@msftorleans 地图上写着:](https://twitter.com/msftorleans) 推特账户发布 Orleans 通知
- 请审查我们的 [会费捐款准则](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 该项目在 [麻省理工学院许可证](https://github.com/dotnet/orleans/blob/main/LICENSE)下获得许可证
