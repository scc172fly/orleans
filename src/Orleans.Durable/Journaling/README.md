<a id="microsoft-orleans-journaling"></a>

# Microsoft Orleans Journaling

<a id="introduction"></a>

## 简介
Microsoft Orleans Journaling 将持久化状态的修改保存为有序日志数据，可通过重放恢复内存中的持久化集合和值。

该包包含由 System.Text.Json 支持的 JSON Lines 存储格式，并默认使用它。请搭配 Microsoft.Orleans.Durable.AzureStorage 等 Journaling 存储提供程序使用。存储提供程序与序列化格式相互独立：Microsoft.Orleans.Durable 提供日志格式及键控的持久化条目编解码器，持久化状态组件使用这些编解码器编码和恢复自身的操作。

应用程序代码使用 `IDurableStateManager` 管理 Grain 的持久化状态、声明其状态组件并确认写入。
`IStateMachine` 定义状态实现所需的底层重放和快照协议。
`IJournaledStateManager` 是用于日志所有权及生命周期操作的独立契约。

<a id="volatile-storage-snapshot-thresholds"></a>

## 易失性存储的快照阈值

`AddVolatileJournalStorage(options => ...)` 可配置
`VolatileJournalStorageOptions.MaxAppendsBeforeSnapshot`（默认 100 次）及
`MaxBytesBeforeSnapshot`（默认 1,048,576 字节）。具名提供程序重载
可分别配置每个提供程序。两个值均必须为正数。
提供程序在构造时捕获所配置的限制值。

达到任一阈值时，会请求在下一次日志写入之前创建快照。
替换成功后，两个计数器均重置；快照本身不计入
追加历史。删除和重新创建也会重置历史。单次
追加可以超过字节数阈值。较大的限制将快照复制成本分摊至
更多次更新；较小的限制减少保留的历史及重放工作量。
计数器属于各自的日志，并由同一提供程序内同一日志的各句柄
共享。
保留的内存包括最新快照、截至并包含超过阈值的那次追加在内的
追加编码字节，以及提供程序的记账数据。

<a id="getting-started"></a>

## 入门
要使用此包，请通过 NuGet 安装：

```shell
dotnet add package Microsoft.Orleans.Durable --prerelease
```

<a id="example---configuring-json-journaling"></a>

## 示例——配置 JSON 日志化
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

如果需要自定义 `JsonSerializerOptions`，请通过选项配置管道配置 `JsonJournalOptions`：

```csharp
siloBuilder
    .AddAzureBlobJournalStorage()
    .Configure<JsonJournalOptions>(options =>
    {
        options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.AddTypeInfoResolver(JournalJsonContext.Default);
    });
```

提供程序注册会调用 `JournalingHostingExtensions.AddJournaling` 注册核心服务、
格式、状态工厂及激活实例的生命周期集成。对于应用程序提供的存储提供程序，使用 `AddJournalStorage<TProvider>`。
`AddJournaling` 用于核心配置；提供程序注册
提供后备存储。

在 Grain 类上应用 `[JournalStorageProvider("orders")]`，可选择一个具名
提供程序，例如使用 `AddAzureBlobJournalStorage("orders", options => ...)` 注册的提供程序。
未明确指定提供程序的 Grain 类型使用 `Default`。`DurableGrain` 辅助方法、
注入的持久化值和集合，以及以 Journaling 为后端的持久化状态，均
共享该激活实例的管理器及所选存储。所有托管该 Grain 类型的 Silo 都必须注册相同的名称及
物理命名空间。

恢复过程使用已有的日志标识读取所选提供程序的物理命名空间。
为具有现有日志的 Grain 类型更改提供程序选择，
需要明确的数据迁移或切换策略，包括回滚方案。

JSON Lines 是默认的 `JournaledStateManagerOptions.JournalFormatKey`。存储提供程序通过 `IJournalMetadata.FormatKey` 和 `JournalMetadata.FormatKey` 公开已存储日志的格式键。恢复时，Orleans 使用存储的键选择匹配的日志格式及持久化操作编解码器。若非空日志未存储格式元数据，Orleans 出于兼容性将其视为旧版 OrleansBinary 数据。

如果已有使用 OrleansBinary 格式写入的数据，可以在规划迁移时继续使用它：

```csharp
siloBuilder
    .AddAzureBlobJournalStorage()
    .ConfigureServices(services =>
        services.Configure<JournaledStateManagerOptions>(options =>
            options.JournalFormatKey = "orleans-binary"));
```

要迁移到 JSON，请将 `JournaledStateManagerOptions.JournalFormatKey` 配置为 `JsonLinesJournalFormat.JournalFormatKey`，并调用 `JsonJournalHostingExtensions` 提供的 `UseJsonJournalFormat(...)`。当 Grain 恢复的数据所采用的格式与配置的写入格式不同时，下一次写入会强制执行完整快照，从而使用 JSON 重写日志，并更新存储格式元数据。

<a id="example---using-durable-state"></a>

## 示例——使用持久化状态
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

字段初始化器在构造期间声明 Grain 状态的购物车组件。由
`AddJournaling` 注册的标准状态管理器在使用激活实例的
`IGrainContext` 构造时，在依赖解析返回前，将自身一次性注册到 Grain 生命周期中。在 `SetupState` 阶段进行的恢复在 `OnActivateAsync`
及 Grain 请求之前完成。普通 `Grain` 子类、应用程序自有的 Grain 基类、`IGrainBase`
实现以及激活实例作用域的功能均可注入 `IDurableStateManager`。注册日志
存储使相应服务可用；只有解析该管理器的激活实例才会执行各 Grain 自身的
日志 I/O。

`DurableGrain` 仍是一个便利基类，通过
`StateManager` 公开相同的应用程序接口，并转发其受保护的 `WriteStateAsync` 辅助方法。Grain 作用域的管理器在
依赖解析返回前注册到生命周期中。自定义管理器替代实现应在其构造函数或
注册工厂中完成该工作，注册其 `ILifecycleParticipant<IGrainLifecycle>`，或直接订阅其
初始化和关闭回调。

默认 Grain 管理器在同一个激活实例作用域的对象上实现两个独立契约：`IDurableStateManager` 和
`IJournaledStateManager`。自定义 Grain 管理器替代实现应按需
提供两个契约及其注册。

共享激活初始化操作在构造之后同步运行，并可首次解析管理器
或其他状态组件。管理器通过现有的生命周期参与机制进行订阅。
在构造期间或同步激活初始化期间解析 Grain 所拥有的管理器并声明状态组件，
使其订阅在生命周期启动前建立。如果生命周期订阅失败，
构造函数释放失败的管理器及其写入器，再传播失败。
使用已恢复状态的功能应在 `SetupState` 之后运行；
应用程序激活之前需要执行的工作应在 `Activate` 之前运行。将调用方的取消令牌
贯穿异步 Grain 操作和状态管理器操作。

`GetOrAddState<TState>(name)` 接受应用程序契约，例如
`IDurableDictionary<string, int>`，并使用已注册的工厂构造对应的状态机
实现。`DurableStateManagerExtensions` 提供 `GetOrAddDictionary`、`GetOrAddList`、
`GetOrAddQueue`、`GetOrAddSet`、`GetOrAddLog`、`GetOrAddValue`、`GetOrAddTaskCompletionSource` 和
`GetOrAddPersistentState` 辅助方法。

在初始化之前，于构造或同步激活初始化期间声明新的状态组件。初始化成功后，
即可读取和修改其恢复的内容。之后的 `GetOrAdd` 请求解析现有名称；对缺失名称的请求
会在修改注册表之前立即失败。`TryGetState<TState>` 执行
仅查找而不创建的操作。对于运行时变化的标识，使用已声明的持久化字典中的键。

同一管理器内的名称使用序数比较。重复请求兼容契约会返回
同一实例；契约或封闭泛型类型不兼容时，会立即失败。键控注入
`[FromKeyedServices("cart")] IDurableDictionary<string, int>` 与调用
`stateManager.GetOrAddDictionary<string, int>("cart")` 返回同一对象，无论哪个途径先解析它。

内置的持久化状态组件自动使用配置的 JSON 编解码器。配置 `JsonJournalOptions` 可控制条目有效载荷所使用的 `JsonSerializerOptions` 实例。日志化命令名称及记录结构由存储格式固定，因此序列化器的命名策略仅影响用户有效载荷的值。

对于裁剪和 Native AOT，使用 `Configure<JsonJournalOptions>(...)` 配置 `SerializerOptions.TypeInfoResolver`、`SerializerOptions.TypeInfoResolverChain`，或使用 `JsonJournalOptions.AddTypeInfoResolver(...)`，为每种写入日志的键、值及状态类型提供源生成元数据。如果还需要明确启用 JSON 格式，推荐使用便捷的 `UseJsonJournalFormat(JournalJsonContext.Default)` 重载。如果元数据不可用，JSON 持久化条目编解码器会因配置错误而失败，不会回退到基于反射的序列化。

<a id="observing-state-and-streaming-drafts"></a>

## 观察状态及流式草稿

Grain 将其持久化状态的观察流公开为 `IAsyncEnumerable<T>` 方法：

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

`DurableStateWatchExtensions.WatchAsync` 用于观察持久化值。观察及日志读取在包含修改的日志写入
获得确认后投递修改，因此读取方只观察到持久化状态。每次
已确认的写入发布一个水位标记，由该管理器的所有观察方共享。

每次写入捕获所有状态，持久化所捕获的内容，然后确认。写入进行期间发生的
修改属于下一次写入，因此 `IStateMachine.OnWriteCompleted` 实现和
`IDurableTaskCompletionSource<T>.Task` 仅报告已完成写入所捕获的内容。如果
另一个线程使日志条目保持打开（例如独立管理器的所有者可以这样操作），写入会等待该条目
关闭，然后将其与其他修改一并捕获，因此 `WriteStateAsync` 确认调用它之前暂存的
每项修改。

值观察投递最新已提交的值，不保留积压记录。管理器在第一个观察开始时创建其观察器
状态，未被观察的状态不发布任何内容。观察需要已恢复的
管理器。若在值处于暂存状态时开始观察，则下一次写入之后投递该值。

`IDurableLog<T>` 是仅追加日志，具有持久化且单调递增的 `long` 类型序列号。
`Append` 返回条目的序列号，`CommittedSequence` 标记持久化条目的末尾。
`TrimBefore` 限制保留范围；`Clear` 替换条目并消耗一个序列号，因此
同一历史内绝不复用序列号；`Seal` 持久化地结束日志。`Epoch` 标识历史：
日志创建或其日志被删除后的首次修改会开始一段新的历史，并具有新的随机世代标识，
该标识与修改一同写入日志。读取方的位置由世代标识和序列号组成，每个 `LogEvent<T>`
都携带其 `Epoch`。`ReadAsync(fromSequence, epoch, includeDrafts, resumeDraftId)` 从指定位置重新读取已提交
条目并等待新条目，因此读取方接收的每个条目都是有效的恢复点。每个活动读取方在条目被裁剪时收到 `Trimmed`，
在其已观察的条目被替换时收到 `Reset`，例如执行清空、删除日志，或在任意激活实例上使用其他历史的
世代标识恢复读取，或从已清除序列号及之前的位置恢复读取。流在投递完开始读取时已提交的条目后
投递一次 `CaughtUp`，并在封存操作持久化后以 `Sealed`
结束。

`BeginDraft` 打开草稿，其 `Write` 写入的项会立即到达传入 `includeDrafts: true` 的读取方。`Commit` 将零个
或更多条目作为一条日志记录追加，并通过 `DraftCommitted` 报告已提交的范围；不带
条目的 `Commit()` 会正常关闭草稿。`Abandon` 或释放操作报告带有
`LogDraftAbandonReason` 的 `DraftAbandoned`。关闭草稿会释放其内容项，因此尚未读完所有项的读取方
接下来收到草稿的结果，不再收到剩余内容项；已提交的条目携带最终内容。
日志被删除或封存时会关闭打开的草稿，因此在提交可能被日志关闭的草稿之前，
应检查 `IsOpen`。日志将草稿标识与已提交的条目一起写入日志，所以
恢复已提交草稿的读取方在任意激活实例中均可收到 `DraftCommitted`，前提是日志仍保留首个
已提交条目。恢复当前激活实例已放弃的草稿的读取方会收到放弃原因；
日志记录最近放弃的 16 份草稿的原因。恢复由另一
激活实例打开且从未提交的草稿的读取方会收到带有 `PreviousActivation` 的 `DraftAbandoned`。

日志写入失败会使观察流进入故障状态；管理器停止会使观察流完成，但不产生 `Sealed`。

要测试使用持久化状态的代码，将其托管在 `InProcessTestCluster` 的 Grain 上，让其 Silo 调用 `AddJournaling()`，
应用该应用程序的日志格式配置，并调用 `AddVolatileJournalStorage()`。Silo 在
内存日志之上运行真实的日志化状态管理器。

<a id="custom-state-and-standalone-ownership"></a>

## 自定义状态及独立所有权

通过在 `IServiceCollection` 上调用
`services.AddStateMachine<TState, TImplementation>()` 注册面向 Grain 的状态组件契约及其实现。
两个类型参数均为引用类型，实现类型同时实现 `TState` 和 `IStateMachine`。
在 Silo 配置中，使用 `siloBuilder.AddJournaling()` 进行核心配置，并使用
`siloBuilder.Services.AddStateMachine<TState, TImplementation>()` 设置 Grain 状态组件映射。
存储提供程序注册已在内部调用 `AddJournaling`。Grain 代码利用现有激活实例作用域，通过
`IDurableStateManager.GetOrAddState` 获取组件。管理器负责
构造、注册及日志流绑定；构造函数接收
依赖项，将注册留给管理器处理。不受支持的应用程序契约会在调用
`GetOrAddState` 时产生明确的注册错误。

不带参数的注册重载通过依赖
注入解析构造函数的依赖项。对于需要感知名称的构造，请使用接受
`Func<IServiceProvider, string, TImplementation>` 的重载：该回调接收拥有该组件的服务
提供程序及请求的状态名称。

`IStateMachine` 包含以下成员：

- `Reset`：重置内存状态并绑定 `JournalStreamWriter`。
- `ReplayEntry`：从已记录操作重建状态。
- `PrepareWriteAsync`：为待提交状态建立异步持久化前置条件。
- `WritePendingEntries`：将待处理操作输出到提供的写入器。
- `WriteSnapshot`：将可用于重建状态的快照输出到提供的写入器。
- `OnRecoveryCompleted`：在应用程序使用前完成重建。
- `OnWriteCompleted`：发布依赖存储确认的效果。

基于命令的状态可以使用 `IDurableValueCommandCodec<TCommand>`，在本地应用命令之前对
完整命令进行编码。快照是在相同应用程序自定义架构中的一条命令，
用于重建整个状态。为命令数据添加 Orleans 序列化标注，
使用 JSON 编解码器时提供 System.Text.Json 元数据。

恢复会重置状态机并重放持久化条目。如果初始化失败，可以在
同一管理器和已注册状态上重试。`JournalReplayContext.ResolveStateMachine` 将条目路由到
其日志流对应的状态机。

写入准备在捕获追加内容或快照之前运行。
各状态以幂等方式准备自身的前置条件。异步准备完成后，
已准备的前置条件可同步使用；管理器随后重新检查所有状态，
以纳入交错执行轮次中暂存的修改。完整的同步准备过程直接进入
捕获阶段。准备失败会保留待提交的修改，供显式
重试写入，并报告 `JournaledStatePreCommitException`。所有者的关闭令牌
取消准备过程，释放操作等待它实际结束。

`IJournaledStateManager` 独立于面向 Grain 的 `IDurableStateManager`，并继承
`IAsyncDisposable`。其所有者 API 提供 `RegisterStateMachine`、`TryGetStateMachine`、
`InitializeAsync`、`WriteStateAsync`、整个日志的 `DeleteStateAsync`，以及 `PendingWriteByteCount`。
`IJournaledStateManagerFactory.CreateStandalone(JournalId)` 返回此所有者契约。应使用
调用方提供的依赖项构造状态机组件，并在初始化前注册它们。
以下辅助方法接受调用方拥有的组件，以及在恢复后更新该组件的操作：

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

调用方提供与日志的配置写入格式匹配的组件编解码器，并保留
组件及其依赖项的所有权。独立日志所有者从不创建或释放
DI 作用域。如果组件构造需要作用域，由调用方提供并管理它。释放
所有者会停止日志处理并释放日志资源；组件及依赖项的释放
仍由调用方负责。`TryGetStateMachine` 查找显式注册的组件。

面向 Grain 的 `GetOrAdd` 和键控解析使用现有激活实例作用域。运行时拥有
该作用域，并负责通过 DI 创建的状态组件、依赖项的生命周期和管理器清理。
独立集成（例如 Durable Jobs 分片）显式提供自身状态组件，
并使用所有者接口协调恢复和写入。

通过 `CreateStandalone` 使用显式 `JournalId` 创建的管理器，或在无 Grain 上下文时
直接从存储构造的管理器，即使创建于 Grain 调用内，也仍由调用方负责初始化及释放。
使用前注册其状态组件，并使用操作的取消令牌等待 `InitializeAsync(cancellationToken)`，
或者有意通过指定生命周期所有权的注册方式
提供它们。通过显式日志工厂创建，使故障处理独立于
当前环境的 Grain 上下文，即使调用方之后将管理器加入生命周期也是如此。

<a id="state-identity-and-retirement"></a>

## 状态标识及退役

在不同激活实例和部署之间保持状态名称不变。初始化声明中缺失的流
进入管理器的自动退役宽限期，默认通过
`JournaledStateManagerOptions.RetirementGracePeriod` 设置为七天。恢复在该
期限内保留其条目。在后续激活实例的初始化中重新引入该名称即可恢复它；宽限期结束后的
压实会移除已退役的流。在每次激活时声明所有必须保持活动的状态组件，
即使不会访问它们的内容也要声明。

应用程序和协议的这些命名变更保留了已持久化的状态名称、流标识、命令
标记、格式键值、序列化成员 ID 及提供程序元数据键。在整个部署和回滚窗口期间，
保持已退役流及存储格式的编解码器可用。

<a id="staging-and-failure-boundaries"></a>

## 暂存及故障边界

待提交日志由使用同一状态管理器的所有调用方共享。在操作本地的数据中准备可能失败的工作及外部
确认。当结果可以安全提交后，将其修改应用到
持久化状态，并等待 `WriteStateAsync`。一次确认覆盖管理器范围内的整个日志
批次，包括交错执行的调用方暂存的修改。应用程序负责安排这一转换与其他交错执行操作的
顺序，并使结果不确定时的重试具有幂等性。

写入或删除失败会永久隔离管理器，使排队操作进入故障状态，并请求
停用关联的 Grain。进行中的调用保留已有的内存状态，而后续
状态管理器操作会明确失败。新的激活实例恢复实际的持久化结果。
对于通过 `IJournaledStateManagerFactory` 创建的管理器，释放失败的实例，并为同一
`JournalId` 创建另一个管理器，在初始化之前显式构造并注册全新的状态组件，
并根据已分配的生命周期使旧组件及依赖项退役。

初始化失败会向本次尝试的调用方报告错误，并使管理器保持未初始化状态。
再次调用 `InitializeAsync`，使用现有的重置与重放契约从头重试。
并发调用方共享正在进行的尝试；恢复成功后写入操作可用。
初始化首次开始后，状态注册始终保持关闭。

取消调用方的等待不会停止已经排队的写入。决定是否重试应用程序命令之前，应通过写入
确认或新的激活实例观察持久化结果。

<a id="storage-provider-contract"></a>

## 存储提供程序契约

日志是持久化状态的权威数据源。每个 `IJournalStorage` 实现都提供以下保证：

- `AppendAsync` 使其完整数据段在所有此前的数据段之后可见，或者使整个数据段都不可见；`ReplaceAsync` 将其完整值作为日志的唯一数据段发布，或者让日志保留先前内容。跨多条记录或多个请求存储的数据段通过单次原子发布步骤变为可见，因此提供程序内部发生崩溃、超时、冲突或重试后，成功的读取仍观察到完整的数据段。
- 超出提供程序限制的数据段会使整个调用以异常失败，数据段的任何部分均不可见。
- 已明确被拒绝的写入保持先前内容不变。结果不确定的写入（例如请求发送后超时）要么应用完整数据段，要么完全未应用。原始请求提交后，重试可能报告前置条件冲突；通过恢复确定其结果。
- 成功的 `ReadAsync` 返回日志的一个版本：截至该版本写入的所有数据段均保持完整且按写入顺序排列。调用方应丢弃失败读取的数据，并从头恢复。
- 每个存储实例持有由首次成功读取或创建建立的写入前置条件（包括观察到日志不存在的情况），并通过自身的写入推进该条件。`AppendAsync`、`ReplaceAsync` 和 `DeleteAsync` 仅在对应版本上成功，否则抛出 `InconsistentStateException`。实例在读取日志或通过 `CreateIfNotExistsAsync` 创建日志之后才能写入；两者均未执行时，通过该实例写入会抛出 `InvalidOperationException`。元数据操作绝不会建立或推进该前置条件。

调用方必须等待同一存储实例的每个操作完成后，才通过该实例启动另一个操作。不同实例通过存储前置条件进行协调。直接使用存储的调用方应在写入前通过 `ReadAsync` 或成功的 `CreateIfNotExistsAsync` 初始化其前置条件。

后续的 `ReadAsync` 调用验证已建立的前置条件，如果另一写入方改变了内容，则抛出 `InconsistentStateException`。这通过同一接口提供历史读取，同时保留活动写入方的前置条件。冲突后的恢复使用新的存储实例。

追加至少包含一个字节。空追加在访问存储之前抛出 `ArgumentOutOfRangeException`；`ReplaceAsync` 接受空数据以发布空日志。

提供程序符合性测试套件（`test/Orleans.Journaling.Tests/Conformance` 中的 `JournalStorageConformanceTests`）通过限制、并发、故障注入和基于模型的测试验证这些保证。本仓库中的每个提供程序均运行该测试套件。

<a id="storage-format"></a>

## 存储格式

JSON 日志化格式将日志条目存储为真正的 JSON Lines：UTF-8 文本，无字节顺序标记，每行一个日志条目 JSON 数组。每行以 `\n` 结尾。恢复接受 LF 和 CRLF 两种换行符。使用格式元数据的存储提供程序应将 `JsonLinesJournalFormat.JournalFormatKey` 存储为格式键，并可使用 `application/jsonl` 作为 MIME 类型。

每条记录的元素 0 包含状态 ID，元素 1 包含持久化操作有效载荷数组：

```json
[8,["set","alpha",1]]
```

在操作有效载荷数组内，元素 0 是命令名称，之后是命令特有的操作数，例如键、值、项数组或版本。存储写入批次追加一条或多条完整 JSON Lines 记录，不添加单独的数据区段信封或最终的容器关闭步骤。

读取现有数据时使用其存储的格式元数据；如果元数据缺失，则按旧版 OrleansBinary 数据读取，并在下一次快照写入时迁移到配置的写入格式。

<a id="catalog-enumeration"></a>

## 目录枚举

`IJournalStorageCatalog.ListAsync` 按提供程序的遍历顺序返回 `IAsyncEnumerable<JournalCatalogEntry>`。
每个条目包含其 `Id` 及可选的 `Metadata`。需要唯一标识时，按 `Id` 去重，
因为重复条目可能携带不同版本的元数据。
`JournalCatalogListOptions.Prefix` 匹配 `JournalId.Value` 原始值的开头，包括不完整的路径
段。例如，`jobs/shards/20260909` 选择该 UTC 日期的带时间戳名称。
使用带尾部斜杠的前缀，例如 `jobs/shards/`，选择命名空间的后代。
`MinId` 和 `MaxId` 分别提供包含边界值的下限和上限。三个约束均使用
`StringComparison.Ordinal`，并在枚举开始时捕获为快照。默认值
表示对应约束无界。互不相交的约束产生空结果。

```csharp
var options = new JournalCatalogListOptions
{
    Prefix = new JournalId("jobs/shards/20260909"),
    MinId = new JournalId("jobs/shards/20260909T1000000000000Z-"),
    MaxId = new JournalId("jobs/shards/20260909T1200000000000Z~"),
    IncludeMetadata = true
};
```

提供程序可以利用上下限的公共前缀进一步缩小原生前缀的范围。
需要统一结果顺序的应用程序使用
`StringComparer.Ordinal` 对选中的 ID 排序。

存储提供程序在内部获取分页结果，并在发现匹配标识时逐个返回它们。`await foreach` 推进遍历，并在循环结束时释放枚举器。分批处理标识的使用方可以跨批次保留一个枚举器，串行推进它，并在最后一个待完成的 `MoveNextAsync` 完成后释放它。使用生命周期覆盖整个枚举过程的取消令牌。

`IncludeMetadata` 默认为 false，并与范围选项一起捕获为快照。为 true 时，提供程序
包含列表操作能够获取的完整元数据：同时观察到的日志格式、存储 ETag 及调用方拥有的
属性，其语义与 `GetMetadataAsync` 相同。Azure Blob 投影
WAL 元数据，Azure Table 投影表头属性，Volatile 在其存储锁保护下捕获元数据快照。
S3 和 Redis 返回 null 元数据，因为其列表操作公开标识而非
完整日志元数据。投影不增加单独的逐日志元数据请求。

需要元数据的使用方使用提供的快照；快照缺失时调用 `GetMetadataAsync`。
空的调用方属性字典是有效的完整快照。将 ETag 视为该快照的
版本并将它传入条件元数据更新；并发修改可能导致更新失败。

| 提供程序 | 发现过程缩小扫描范围的方式 | 剩余工作 |
| --- | --- | --- |
| Volatile | 有序键索引选择覆盖所请求前缀和边界的视图。 | 捕获选中键的快照并检查日志当前是否存在。 |
| Azure Table，默认映射 | 可打印 ASCII ID 的每个字节使用两个大写十六进制数字表示，允许直接使用索引前缀以及键的上下限筛选。 | 查询包含日志表头行条件；服务控制选中键范围内的工作量。 |
| Azure Table，自定义映射 | 规范日志 ID 筛选器限制返回的表头。 | 由于日志 ID 属性未建立索引，任意映射可能需要扫描表。 |
| Azure Blob | `wal/` 命名空间和原始 ID 前缀选择 WAL Blob；保守的 ASCII 边界可以在平面和层次命名空间中安全地缩小遍历范围。 | 扩大的边界和最后一页可能包含其他候选项，需依据原始序数范围筛选。检查点占用独立命名空间。 |
| S3 通用存储桶，已启用有序列举 | `wal/` 命名空间排除检查点。恒等映射的键使用原生前缀和位于包含边界值的下限之前的初始 `StartAfter` 标记，随后在 WAL 键的上限处停止。 | 最后一页可能超出范围。自定义键映射使用配置的原生前缀及标识筛选。 |
| S3 Express 目录存储桶 | `wal/` 内以斜杠结尾的原生前缀限制命名空间并排除检查点。 | 在无序遍历期间筛选部分名称和时间边界。 |
| Redis | 可读的键名支持原生 `SCAN MATCH` 前缀筛选；默认映射可在读取标识元数据之前执行本地键范围检查。 | `SCAN MATCH` 仍然遍历服务器键空间。自定义键映射从匹配的元数据哈希中读取规范 ID。 |

Blob 的原生边界保留共享列举前缀，在标点或
目录分隔符影响存储排序时扩大后缀范围。有序 S3 的原生上下限优化适用于
存储排序与标识的序数排序一致的场景，包括固定宽度 ASCII
时间戳名称。在遍历存储期间，所有原始边界仍会被强制执行。S3 有序列举是通用存储桶的显式
`UseOrderedListing` 能力设置。
选中的标识数量、传输的键数量和后端扫描工作量是不同的成本：
服务器端筛选可以减少传输数据，但服务可能仍检查更广的键空间。

枚举观察实时存储。并发修改遵循各提供程序的列表语义；调用方应容忍修改期间出现重复标识，并通过后续枚举发现之后的更新。日志的存在状态可能在发现与存储操作之间变化。取消及存储错误会通过枚举向外传播。重试列表操作时，应释放失败的枚举器并开始新的枚举。

<a id="provider-metrics"></a>

## 提供程序指标

`Microsoft.Orleans` 指标仪表记录目录遍历及显式提供程序重试：

- `orleans-journaling-provider-catalog-pages` 统计 S3 和 Azure 目录
  遍历中收到的页数，包括空页。
- `orleans-journaling-provider-catalog-items` 统计本地筛选之前的原生分页候选项及已消耗的 Redis
  扫描键数量。
- `orleans-journaling-provider-catalog-entries` 统计 S3、Azure Blob、Azure
  Table、Redis 及 Volatile 提供程序投递的条目数。
- `orleans-journaling-provider-retries` 统计提供程序循环中的显式重试次数。

页数、项数及条目数使用提供程序提供的 `provider` 标签；重试还使用 `reason`。
各提供程序库拥有自己的简称，例如内存提供程序的 `volatile`。
比较候选项与投递条目数量，以评估筛选效果及重复项抑制情况。

现有的状态管理器指标及提供程序特有指标保留存储延迟、结果和字节数
测量。应用程序通过 Aspire 等宿主集成、遥测插桩库或 SDK 诊断配置提供
[客户端 SDK 遥测数据](../../docs/site/src/content/docs/grains/journaling/operations.md#use-host-provided-dependency-telemetry)，
使用这些信号观察依赖项耗时和结果，并使用服务端事务
指标核对计费。

<a id="documentation"></a>

## 文档
有关更全面的文档，请参阅：
- [Microsoft Orleans 文档](https://dotnet.github.io/orleans/docs/)
- [Grain 持久化](https://dotnet.github.io/orleans/docs/grains/grain-persistence/)
- [System.Text.Json 文档](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/overview)

<a id="feedback--contributing"></a>

## 反馈与贡献
- 如遇到任何问题或希望提供反馈，请[在 GitHub 上提交 Issue](https://github.com/dotnet/orleans/issues)
- 加入我们的 [Discord](https://aka.ms/orleans-discord) 社区
- 关注 Twitter 账号 [@msftorleans](https://twitter.com/msftorleans)，获取 Orleans 公告
- 欢迎贡献！请参阅我们的[贡献指南](https://github.com/dotnet/orleans/blob/main/CONTRIBUTING.md)
- 本项目采用 [MIT 许可证](https://github.com/dotnet/orleans/blob/main/LICENSE) 授权
