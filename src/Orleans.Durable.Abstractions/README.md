# Orleans.Durable.Abstractions

> [!IMPORTANT]
> 此预发行 Alpha 包提供仍在积极评估中的 Orleans 持久化任务抽象。
> 随着宿主实现提供反馈，其编程模型可能在不同 Alpha 版本之间
> 发生变化。

此程序集为 .NET 11（`net11.0`）定义 Orleans 持久化异步操作的编程模型，位于
`Orleans.Durable.Tasks` 命名空间。Orleans 宿主提供调度、持久化、
确定性时间及持久化取消。任务定义依赖此抽象包。
声明 `DurableTask` 方法的 Grain 接口项目，以及调用这些方法的客户端，
还需引用 `Microsoft.Orleans.Durable`。

<a id="execution-model"></a>

## 执行模型

- 调用 `async DurableTask` 会创建一个只能使用一次的延迟执行定义。宿主运行该定义时，编译器生成的
  状态机开始执行，并在达到终态后释放捕获的状态。
  每次执行应创建新的实例。使用 `DurableTask.Run` 创建的定义
  可以重复调度。
- 每个根执行实例都有 `TaskId`：即 `WithId` 指定的名称，或在持久化执行实例之外启动
  未命名任务时新生成的随机 ID。子 ID 在父 ID 后追加调用方提供或自动生成的
  路径段。只要未命名子执行实例以相同的确定性顺序创建，自动生成的路径段
  就能在重放时保持稳定。一次 `WhenAll` 或 `WhenAny`
  调用在操作 ID 下为其并发批次分配稳定的索引槽；该操作 ID 由
  `WithId` 命名。彼此独立的并发
  调度操作（包括并发调用组合器）使用显式 `WithId`
  路径段，或以确定性的顺序启动。同一次运行中，若 `WithId` 重复使用某个路径段，
  后续出现的位置按顺序标识为 `line`、`line$2` 等，使循环内命名的子执行实例
  在重放时保持原有 ID。生成的路径段包含 `$`，显式名称不包含该字符。
- `TaskId` 按转义后的层次路径进行格式化和解析，例如 `order-42/charge`，其中 `\`
  用于转义路径段内的 `/` 和 `\`。System.Text.Json 将其序列化为该字符串，可作为值或
  字典键，通过 `TaskIdJsonConverter` 实现，因此 JSON 有效载荷和源生成的上下文无需注册
  即可携带任务 ID。
- 在已有 ID 下调度相同定义，会重新关联该 ID 已记录的响应。
  宿主保留首次与该 ID 关联的定义。
- 等待操作的取消令牌会放弃对应的调度、轮询或等待操作。
  它不会请求取消持久化任务本身。
- `CancelAsync` 请求持久化的*任务取消*，宿主记录该请求后即完成。
  请求是单调且幂等的，宿主将其与任务状态一并保留。取消是
  协作式的：`DurableTask.Current.CancellationToken` 被取消，
  `IsTaskCancellationRequested` 变为 true，请求传递到任务的附接子任务，
  由任务体决定其结果。若取消从任务体向外传播（例如
  等待已取消的子执行实例时），任务以取消状态结束；否则以任务体的结果或
  失败完成。任务体的补偿操作（例如 `DurableTask.RunInline` 效果、受屏蔽保护的子执行实例以及
  日志化状态修改）会提交。
- `CancelAsync(reason)` 为请求附加原因，任务代码可以从
  `DurableTask.Current.TaskCancellationReason` 读取，例如在决定任务结果的 `catch` 块中读取。
  首次请求决定取消原因；宿主将该原因与任务的取消状态一并保留，
  并将其传递给附接子任务。重放会在原运行首次观察到取消请求的位置
  观察到取消，并读取相同的原因。
  请求取消前，以及请求未附带原因时，`TaskCancellationReason` 为 `null`。
  宿主通过 `DurableTaskContext.RequestTaskCancellation` 提供原因。
- 当一次*执行尝试*（任务体的一次内存运行）的工作无法继续记录时，宿主通过
  `DurableTaskContext.CancelAttempt` 取消该尝试：例如
  Grain 激活实例正在停止、任务已中止或删除，或当前代码无法重放
  任务记录的执行过程（`UnsupportedVersion` 或 `Diverged`）。此后，该尝试的每个持久化
  操作均抛出 `DurableTaskAttemptCanceledException`，该异常继承自
  `OperationCanceledException`，并携带 `DurableTaskAttemptCancellationReason`：包括 `RunInline`、启动
  或等待子执行实例（含受屏蔽保护和分离的实例）、`Post`、`ScheduleAsync`、信号、
  延迟、句柄操作及组合器。因尝试取消而结束的等待也会抛出该异常。
  宿主丢弃该尝试的响应；正在停止的激活实例中的任务会在新
  尝试中重放，而无法重放其记录的任务仍处于待完成状态，直到能够
  重放它的代码运行。尝试代码对日志化 Grain 状态的修改（例如清理）
  随 Grain 的下一次日志写入提交。
- `DurableTask.Current.CancellationToken` 在任务取消和尝试取消时都会被取消，
  因此普通任务代码发起的调用在两种情况下都能及时结束。
  `DurableTask.Current.AttemptCancellationToken` 仅在尝试取消时被取消。任务取消后，
  在以 `IsTaskCancellationRequested` 为筛选条件的 `catch` 块中执行持久化清理的代码，
  应将 `AttemptCancellationToken` 传递给其发起的调用，因为
  `CancellationToken` 已被取消；
  [ORLEANS0217](https://aka.ms/orleans/diagnostics#orleans0217) 会报告传递
  `CancellationToken` 的清理代码。进程内清理（例如释放信号量或
  释放资源）应放在两种取消情况下都会运行的 `finally` 块和 `using` 语句中。
- 在任一令牌上注册的都是普通的同步 .NET 观察器，在令牌被取消时运行，
  并迅速返回。
- 延迟使用宿主提供的 `DurableTask.Current.UtcNow`，使重放观察到
  相同的逻辑时间。`DurableTask.DelayUntil` 等待绝对到期时间。
<a id="responses"></a>

## 响应

`DurableTaskResponse` 具有待完成、已订阅、成功、已取消和失败状态。
待完成和已订阅响应尚未完成。成功、已取消和失败响应
均为终态；等待终态响应会返回其值，或抛出记录的
取消或失败异常。宿主的等待操作返回终态响应；`ScheduledTask.WaitAsync` 及返回或逐个产生
已观察句柄的组合器（例如 `ScheduledTask.WhenAny`、`ScheduledTask.WhenEach` 和
`WhenAllCompleted`）在等待返回非终态响应时抛出 `InvalidOperationException`。

<a id="combinators"></a>

## 组合器

`WhenAll` 和 `WhenAny` 接受列表或任意输入序列，并在调用时捕获输入。被等待时，
组合器作为等待它的执行实例的一部分运行，不记录自身的结果。各输入作为子执行实例运行，
以组合器操作标识下的输入索引标识，因此输入顺序稳定时，其标识在重放中也保持稳定。
组合器并发调度输入，使宿主能够
将整个批次一并提交。

每个组合器也是其输入集合上的扩展方法，因此可以直接在列表、数组或查询上
进行组合：

```csharp
var reserved = await lines.Select(line => Catalog(line.ProductId).ReserveAsync(OrderId, line.Units))
    .WhenAll()
    .WithId("reserve");
```

句柄以相同方式组合：`handles.WhenAll(cancellationToken)`、`handles.WhenAny()`、`handles.Race()`、
`handles.WhenAllCompleted()` 和 `handles.WhenEach()`。诸如 `[a, b]` 的集合表达式自身没有类型，因此将其传递给静态形式，
即 `DurableTask.WhenAll([a, b])`。

`WhenAll` 遵循 `Task.WhenAll` 的语义：仅在所有输入均达到终态后完成，包括受到
持久化取消影响的输入。等待它时，若有任何输入失败，则按输入顺序抛出第一个失败异常；
否则若有任何输入被取消，则按输入顺序抛出第一个已取消输入的 `OperationCanceledException`；
否则 `WhenAll<T>` 按输入顺序返回结果数组。根据输入顺序选择异常，因此重放时
无论完成顺序如何，都会观察到同一个异常。`WhenAny` 返回胜出者的输入索引，
`WhenAny<T>` 还返回其结果；胜出者失败或被取消会使组合器失败。胜出者是
最先完成的输入，宿主在重放时选择同一个胜出者：从持久化的
完成顺序派生胜出者，或在确定性的决策 ID 下持久化该选择。`WhenAny` 让其余已调度的
任务继续运行。持久化取消传递到作为附接子任务的输入，组合器等待
这些输入的结果。

`Race` 保持结构化并发：它像 `WhenAny` 一样调度输入并选择胜出者，随后以
`Another task won the race.` 为原因请求持久化取消其余每个输入，并在所有输入
达到终态后完成，因此没有输入会比它存活更久。其返回值与 `WhenAny` 相同；胜出者失败或被取消会使它失败，
其余输入的结果不影响它。`WaitAsync(timeout)` 和 `WaitUntilAsync(dueTime)` 让任务定义（作为
输入 `0`）与持久化 `Delay` 或 `DelayUntil`（作为输入 `1`）竞速。重放保留延迟记录的到期时间，因此
超时从首次运行开始计算。当延迟先到期时，任务收到取消请求，原因为
类似 `The operation timed out after 00:02:00.` 的文本，组合器等待任务的结果：若任务以取消状态结束，
组合器抛出 `TimeoutException`，其内部异常为任务的 `OperationCanceledException`；任何其他
结果（例如任务观察到请求后返回的结果）就是组合器的结果：

```csharp
// The authorization runs as authorize/0 and its timeout as authorize/1.
var payment = await Payments.AuthorizeAsync(order.Total).WaitAsync(TimeSpan.FromMinutes(2)).WithId("authorize");
```

组合器生成的操作标识符（例如 `$when-all-2`）按执行实例中在它之前运行的
组合器计数。`WithId` 为操作命名，因此部署时增减
前面的组合器不会改变其输入的标识符：

```csharp
var reserved = await DurableTask.WhenAll(lines.Select(line => Catalog(line.ProductId).ReserveAsync(OrderId, line.Units)))
    .WithId("reserve");
```

输入的标识为 `{taskId}/reserve/0`、`{taskId}/reserve/1` 等；记录选择的宿主在
`{taskId}/{name}/$winner` 下记录具名 `WhenAny` 的胜出者。`WithId(name).ScheduleAsync()` 将组合器作为
该标识符下的子任务调度，使其作为独立执行实例运行、记录组合后的结果，并在该执行实例生成的
操作标识符下标识其输入。

`ScheduledTask.WhenAll` 像 `WhenAll` 一样等待已调度任务：在所有任务均达到终态后完成，随后按输入顺序抛出
第一个失败异常，或在没有失败时按输入顺序抛出第一个取消异常。`ScheduledTask.WhenAny` 返回
最先完成的任务，包括失败或已取消的任务。在持久化执行实例内，宿主像选择
`WhenAny` 胜出者一样，根据持久化完成顺序，或根据记录在
生成的 `$when-any-N` 操作标识符下的 `$winner` 选择来确定它，使每次重放都返回同一任务。在持久化执行实例之外，
它返回最先收到持久化响应的任务。调用时即捕获候选项，因此即使调用方之后复用或更改输入集合，
选定任务的标识也不变；返回的任务已观察到
其终态响应。调用方取消和宿主等待失败均会向外传播。在持久化执行实例之外，选择完成后，
它会取消并等待未胜出的等待操作结束，而不会取消对应的持久化任务。

`ScheduledTask.Race` 像 `ScheduledTask.WhenAny` 一样选择胜出者，在持久化执行实例内使用生成的 `$race-N` 操作标识符，
随后请求持久化取消其余任务，并在所有任务
达到终态后返回胜出者，使每个句柄的结果均可观察。句柄的 `WaitAsync(timeout)` 和 `WaitUntilAsync(dueTime)`
返回 `Task` 和 `Task<T>`，结果与任务定义上的重载一致。在持久化执行实例内，超时是
持久化延迟，由该执行实例在操作标识符 `{segment}$timeout-N` 下以 `1` 调度。该标识符由
句柄的路径段及其限时等待次数决定，因此其他任务上的操作不会使其发生偏移，由宿主
选择胜出者；在持久化执行实例之外，超时使用系统时钟计时，到期时向句柄发出取消
请求。这些方法的取消令牌放弃等待，而不取消任务。每类未命名的
组合器独立计数其操作，例如 `$when-any-1` 和 `$when-any-2`。

<a id="scheduled-task-handles"></a>

## 已调度任务句柄

`ScheduledTask` 是已启动的 `Task` 的持久化对应物：它携带任务 ID 并观察其响应。
等待、轮询或组合器一旦观察到终态响应，句柄就会缓存它，因此 `IsCompleted`、`Status`
和 `Exception` 可同步检查任务，`ScheduledTask<T>.GetResult()` 返回结果或抛出
记录的失败或取消异常，与等待一致。`TryGetResult(out value)` 对成功完成的任务返回 `true`，
并给出其结果。在句柄观察到终态响应之前，`Status` 为 `Pending`。客户端使用
`IsCompletedAsync`、`GetStatusAsync` 和 `GetResponseAsync(PollingOptions)` 进行轮询；持久化任务体则等待
句柄或使用持久化超时等待，其轮询操作抛出 `NotSupportedException`
（[ORLEANS0225](https://aka.ms/orleans/diagnostics#orleans0225)）。

句柄的 `Id` 命名启动或观察任务的执行实例中的操作，供组合器和重放
寻址；其 `TargetTaskId` 命名宿主上的任务，供其他参与方寻址。除非
宿主以另一标识符存储任务，否则两者相等：分离任务的句柄报告其根 ID，宿主可以通过
`IScheduledTaskHandle.TargetTaskId` 将调用方发起的任务限定在该调用方的作用域内。宿主还通过功能描述任务，
`ScheduledTask.GetFeature<TFeature>()` 从 `IScheduledTaskHandle.GetFeature<TFeature>()` 返回这些功能，
例如其他参与方用于访问任务的地址。

`DurableTask.ScheduleAll(inputs)` 并发调度所有输入，使宿主可以在一次写入中提交该批次，并
按输入顺序返回句柄而不等待结果。`DurableTask.WhenAllCompleted(inputs)` 还会等待
所有输入达到终态，并返回各自带有已观察结果的句柄；输入失败或取消
不会使它失败，而尝试取消会使它失败。`ScheduledTask.WhenAllCompleted(tasks)` 以相同方式等待现有句柄。
其输入的标识方式与 `WhenAll` 输入相同，位于 `$schedule-all-N` 或 `$when-all-completed-N` 下，或
位于 `WithId` 指定的路径段下。句柄属于等待它的执行实例，因此这些组合器仅在
持久化执行实例内被等待时运行，使用 `ScheduleAsync()` 将它作为独立任务调度会抛出
`NotSupportedException`。

`WhenAllCompleted` 可精确补偿已成功的输入：

```csharp
var reservations = await DurableTask.WhenAllCompleted(
        lines.Select(line => Catalog(line.ProductId).ReserveAsync(OrderId, line.Units)))
    .WithId("reserve");
var reserved = lines.Where((line, i) => reservations[i].TryGetResult(out var ok) && ok).ToList();
if (reserved.Count < lines.Count)
{
    await DurableTask.WhenAll(reserved.Select(line => Catalog(line.ProductId).ReleaseAsync(OrderId))).WithId("release");
}
```

<a id="completion-order"></a>

## 完成顺序

`ScheduledTask.WhenEach(tasks)` 以 `IAsyncEnumerable` 形式按完成顺序逐个产生任务，每个任务仅产生一次；
`DurableTask.WhenEach(inputs)` 在开始枚举时，在生成的 `$when-each-N` 操作标识符下调度输入，
再以相同方式逐个产生它们。已产生的句柄已观察到其终态响应；失败或
取消不会使枚举失败。枚举未遍历到的输入继续运行。枚举的
取消令牌放弃调度及等待输入，而不取消输入任务。

在持久化执行实例内，宿主选择下一个任务：在持久化完成顺序中最早完成的剩余任务。
每次重放都以相同的顺序产生任务，包括已有更多任务完成的重放，因此
按结果到达顺序执行操作的代码在重放时执行相同的步骤：

```csharp
var quotes = await DurableTask.ScheduleAll(suppliers.Select(supplier => Supplier(supplier).QuoteAsync(order)))
    .WithId("quotes");
await foreach (var quote in ScheduledTask.WhenEach(quotes))
{
    if (quote.TryGetResult(out var offer) && offer.Price <= budget)
    {
        return await Supplier(offer.SupplierId).AcceptAsync(order).WithId("accept");
    }
}
```

`ScheduleAll(...).WithId(name)` 像此示例一样为输入命名。在持久化执行实例之外，
`ScheduledTask.WhenEach` 按等待操作观察到响应的顺序产生任务。

<a id="starting-and-observing-tasks"></a>

## 启动及观察任务

定义上的配置决定任务的性质，启动任务的调用方式决定调用方何时
继续执行：

| | `await definition` | `await definition.ScheduleAsync()` | `definition.Post()` |
| --- | --- | --- | --- |
| 附接（默认） | 运行子执行实例，在结果持久化后返回其结果。 | 宿主接受并持久化存储子执行实例后返回其句柄。 | 将子执行实例暂存到该执行实例的下一次持久化写入中，并返回其句柄。 |
| `.Shielded()` | 同附接方式，但调用方的取消不会传递给子执行实例。 | 同附接方式，但调用方的取消不会传递给子执行实例。 | 同附接方式，但调用方的取消不会传递给子执行实例。 |
| `.Detached()` | 启动独立根任务并返回其结果；取消等待只会结束调用方的订阅。 | 宿主接受并持久化存储根任务后返回其句柄。 | 将根任务暂存到调用方的下一次持久化写入中，并返回其句柄。 |
| `.Detached(rootId)` | 同 `.Detached()`，使用根 ID `rootId`。 | 同 `.Detached()`，使用根 ID `rootId`。 | 同 `.Detached()`，使用根 ID `rootId`。 |

- 在持久化执行实例内，附接子任务属于该执行实例的树：该执行实例拥有它，
  持久化取消会传递给它，删除该执行实例的树也会删除它。
- `.Shielded()` 阻止执行实例的取消传递给子执行实例，但该执行实例仍
  拥有并等待它；请参阅[补偿已取消的工作](#compensating-canceled-work)。
- `WithId(name)` 为执行实例中启动任务的操作命名，并按出现次数标识，例如
  `reserve$2`。启动时才分配标识符，因此各配置调用的顺序不改变
  它；每次使用某项配置启动时都会获得下一个出现次数对应的标识。
- `.Detached()` 启动独立根任务。启动它的执行实例可以在它仍运行时完成，
  并且该执行实例的取消、中止和删除不会传递给它。启动操作将该执行实例指定为
  订阅者，因此等待任务或其句柄即可观察其结果，无需单独
  订阅；取消该等待只会结束订阅。其根 ID 由宿主的
  标识（宿主通过 `DurableTaskContext.HostId` 提供，例如其 Grain）、
  执行实例 ID 以及启动它的操作 ID（`WithId` 指定的名称，或该执行实例的
  下一个生成的 ID）派生。根 ID 是单个路径段 `{hostId}/{executionId}/{segment}`，其中各
  部分像 `TaskId` 一样转义其中的 `/` 和 `\`，因此不同宿主、执行实例及操作会派生出
  不同的根 ID，重放则派生出相同的 ID。分离任务的定义必须能够由宿主
  作为根任务启动，例如持久化 Grain 调用，因为宿主会独立存储和运行它；
  步骤、`async DurableTask` 方法及组合器作为启动它们的执行实例的一部分运行，
  以分离方式启动这些定义会抛出 `InvalidOperationException`
  （[ORLEANS0228](https://aka.ms/orleans/diagnostics#orleans0228)）。
- `.Detached(rootId)` 启动独立根任务 `rootId`，其他参与方可事先获知并
  寻址它，例如 `place-order-42`。从任何执行实例或调用方（包括重放）在该任务宿主上启动
  `rootId`，都访问同一任务。
- `ScheduleAsync()` 在任务宿主已持有该任务后返回，因此其 ID（句柄的 `TargetTaskId`）
  可以交给其他参与方，由它们使用 `GetDurableTask` 等查找操作寻址。若宿主拒绝启动（例如
  ID 已标识另一种定义），启动即失败。在持久化执行实例内，执行实例
  通过等待句柄、将句柄传递给组合器或返回句柄，来观察自己调度的每个附接子任务。
- `Post()` 是同步操作：它暂存启动请求，该请求随调用方的下一次持久化
  写入，与调用代码所做的状态修改一起提交。随后宿主投递启动请求，
  重试直至任务宿主接受；任务宿主根据任务的
  ID 对投递去重，使任务仅启动一次。若任务宿主不接受启动请求（例如请求
  在宿主的接纳窗口结束后到达），句柄将以失败状态结束。
  在持久化执行实例内，启动操作像 `DurableTask.RunInline` 效果一样被记录，因此重放时
  不再启动任务，而是返回同一个句柄，之后等待该句柄可观察
  结果。没有持久化发件箱的代码（例如客户端）无法使用 Post 操作。
- 在持久化执行实例之外，每个任务都是根任务，因此 `.Detached()` 不产生变化。`WithId`
  和 `.Detached(rootId)` 提供根 ID，使启动可安全重试：调用方在
  超时等结果不确定的失败后重试，会访问同一任务。未命名的启动请求获得
  新的随机根 ID，由句柄的 `Id` 报告，其行为类似普通调用。

启动后续工作并在不等待该工作的情况下完成的工作流，可以通过 Post 操作启动分离任务：

```csharp
order.PlaceOrderAsync(request).Detached($"place-order-{orderId}").Post();
```

<a id="signals"></a>

## 信号

`DurableSignal<T>` 命名向正在运行的任务传递值的事件，例如发货
通知或人工审批。发送方和等待方共享同一个描述符：

```csharp
static readonly DurableSignal<Approval> Approved = new("approved");

var approval = await Approved.WaitAsync();
```

- 信号寻址一个已调用的任务：即客户端、其他 Grain 或以分离方式启动它的持久化执行实例等
  调用方所启动的任务。该任务执行过程中任意位置的等待
  都可观察此任务的信号，包括任务等待的
  `async DurableTask` 步骤中的等待，以及 `WhenAny` 输入中的等待。每个已调用任务有
  自己的信号。
- 每个名称在每个任务中锁存一个值。宿主将首个值与任务状态一起记录，
  并将其返回给该名称的每次等待，包括重放期间的等待。重复发送相等值的信号
  不改变任务，而不同值的信号会被拒绝。
  `For(suffix)` 为重复事件的每次出现派生不同的名称，
  例如 `approved/round-2`。
- 在对应等待之前到达的信号会被记录，等待立即返回该值。
- 直接等待 `WaitAsync()` 时，它作为等待它的执行实例的一部分运行，因此不创建子
  任务。`TryWaitAsync(deadline)` 通过 `Race` 让等待与 `DelayUntil(deadline)` 竞速，信号胜出时返回
  `(true, value)`，截止时间胜出时返回 `(false, default)`。竞速会取消
  另一个输入，并在两者均达到终态后完成。宿主在重放时选择同一个胜出者，所以重放
  返回相同的结果；`WithId` 像命名其他组合器一样命名此次竞速：

```csharp
var (received, approval) = await Approved.TryWaitAsync(deadline).WithId("approval");
```

- 截止时间胜出后，信号仍保留其锁存机制，因此后续 `WaitAsync()` 可返回
  截止时间之后到达的值。作为 `WhenAny` 的输入时，等待与其他输入竞速；信号胜出后，
  再次等待 `WaitAsync()` 返回已记录的值。
- 等待方执行实例的持久化取消会取消其等待。

<a id="compensating-canceled-work"></a>

## 补偿已取消的工作

已取消的工作流会继续运行直到任务体完成，因此能够撤销已执行的工作。
它通过 `.Shielded()` 启动补偿：受屏蔽保护的子执行实例运行至完成，工作流像往常一样等待
其结果：

```csharp
public async DurableTask<OrderResult> PlaceOrderAsync(Order order)
{
    await inventory.ReserveAsync(order.Id).WithId("reserve");
    try
    {
        return await payment.ChargeAsync(order.Id, order.Total).WithId("charge");
    }
    catch (OperationCanceledException) when (DurableTask.Current.IsTaskCancellationRequested)
    {
        await inventory.ReleaseAsync(order.Id).WithId("release").Shielded();
        return OrderResult.Canceled(DurableTask.Current.TaskCancellationReason);
    }
}
```

- 受屏蔽保护的子执行实例（例如本地步骤、延迟或对另一个 Grain 的调用）即使工作流
  已收到取消请求也会运行，并且工作流的取消之后也不会传递给它，
  包括重放后。工作流自己的 `CancellationToken`、`IsTaskCancellationRequested` 和
  `TaskCancellationReason` 仍报告其取消状态。管理性中止仍会停止受屏蔽保护的
  子执行实例，删除工作流执行树时也会删除它。通过其自身的句柄取消它，
  会取消它及其子执行实例。
- 受屏蔽保护的子执行实例的子执行实例仅受到该子执行实例自身取消的影响，因此受屏蔽保护的
  `DurableTask.Run` 步骤及其启动的工作会运行至完成。受屏蔽保护的组合器作为
  工作流的一部分运行，并屏蔽其启动的每个输入。
- 每个被等待的持久化操作都以相同方式遵循 `.Shielded()`，无论它作为子执行实例运行，还是作为
  工作流的一部分运行：受屏蔽保护的信号等待、`TryWaitAsync`、延迟、`GetDurableTask` 订阅、
  `Race`、`WhenAny` 或 `WaitAsync` 都会在工作流取消后继续直至完成，只有
  尝试取消才会结束它。通过
  `await Paid.WaitAsync().Shielded()` 等待支付的工作流，仍会收到
  取消后到达的支付结果，并决定其含义。
- 必须比工作流存活更久的工作（例如观察到工作流结果后仍继续进行的清理）
  作为分离任务运行，工作流的取消同样不会传递给它：
  `inventory.ReleaseAsync(order.Id).Detached().Post()`。
- 宿主会及时将已取消任务的叶节点置为取消终态：包括延迟、任务体尚未
  启动的子执行实例，以及请求之后调度且未受屏蔽保护的附接子任务，
  这些附接子任务不会运行。已完成的子执行实例保留记录的结果，正在运行的子执行实例
  自行观察取消请求。
- 已取消任务的重放在原运行首次观察到请求的位置观察该请求，宿主为每个执行实例记录此位置，
  因此任务体采取相同的执行路径，其后的清理操作
  也根据记录重放。

<a id="aborting-tasks"></a>

## 中止任务

`ScheduledTask.AbortAsync(reason)` 供运维人员使用，例如任务体忽略取消或
清理过程卡住时。宿主将任务及其附接的后代任务置为失败终态，异常为
`DurableTaskAbortedException`，其消息就是中止原因；同时以
`TaskAborted` 为原因取消这些任务正在运行的尝试，并丢弃尝试的响应。尝试中的持久化操作会抛出异常，
而 `IsTaskCancellationRequested` 保持 `false`，因此不会执行任务取消补偿。等待已中止的任务
会抛出 `DurableTaskAbortedException`。中止
已达终态的任务不会改变它；分离任务是独立的根任务，需要
单独中止。

<a id="writing-replay-safe-workflows"></a>

## 编写重放安全的工作流

宿主会重放尚未完成的执行实例，例如在其 Grain 停用后：
方法体从头开始重新运行，每个已记录操作返回其记录的结果。
如果每次重放都以相同的顺序，使用相同的标识符和参数调度相同的操作，
工作流就能正确恢复执行。

重放不会重建 Grain 的日志化状态，该状态包含最新已提交的修改，
包括后续操作所做的修改。因此，持久化任务体包含三类代码：

- **已记录操作**在每个执行实例中执行一次，重放返回其记录的结果：
  包括持久化 Grain 调用、`async DurableTask` 方法、`DurableTask.Run` 委托及
  延迟等子执行实例，以及信号等待、分离启动、Post 启动及 `DurableTask.RunInline` 效果。
- **重新执行的代码**位于任务体最后一个已记录操作之前，在每次重放中重新运行。
  它依据方法参数、记录的结果、`DurableTask.Current.UtcNow`、
  取消状态以及在执行实例待完成期间保持不变的配置计算值。
- **同步尾部**位于任务体最后一个已记录操作之后，任务体返回前不再出现其他 `await`。
  它仅在任务体完成的那次运行中执行。实现了
  `DurableTaskContext.OnBodyCompleted` 的宿主将其状态修改与任务
  结果在同一次写入中提交，因此重放不会再次执行它。

重新执行的代码应遵循以下规则：

- 将每项副作用放在已记录操作或同步尾部中。修改 Grain 状态、读取会决定后续操作的 Grain 状态，
  以及产生随机值、标识符、
  时钟和环境值时，应在 `DurableTask.RunInline`、子执行实例或同步尾部中进行，使重放
  观察到首次运行记录的值。通过 `DurableTask.Current.UtcNow` 读取逻辑时间。
  若同步尾部的结果尚未提交，则尾部及其中的效果会再次运行，因此同步尾部发送给外部服务的值
  （例如幂等键）应根据任务的标识及其本次运行派生，
  例如 `$"{grainId}/{DurableTask.Current.Id}/{DurableTask.Current.Incarnation:N}"`。
  运行世代标识用于区分同一任务标识的后续运行，使该次运行的操作取得新
  键。在子执行实例中调用外部服务，其外部效果需要自身的幂等性保障。
- 在日志化 Grain 状态中存储不可变值（例如记录类型及不可变集合），
  并通过赋予新值进行修改。状态 API 记录每次赋值；直接就地修改
  状态持有的对象会绕过该 API。
- 使用 `WithId` 为有条件调度、在循环中调度、在组合器之外并发调度的子执行实例，以及
  组合器指定稳定的标识符。只有未命名的
  子执行实例、未命名的 `DurableTask.RunInline` 效果、未命名的分离任务及组合器按相同顺序创建时，生成的标识符才稳定。
- 如果重放在先前运行已记录的标识符下调度另一项操作或使用不同参数，
  就会偏离记录的执行过程。宿主以
  `DurableTaskAttemptCancellationReason.Diverged` 取消尝试，并使任务保持待完成状态，直到与
  记录匹配的代码重放它。
  当部署在执行实例待完成期间更改工作流时，应保留现有步骤
  和标识符，并为新步骤分配新标识符，或通过
  `DurableTask.GetVersion` 对变更进行分支；参阅[版本化变更](#versioned-changes)。修改后的 `RunInline` 委托
  不影响已记录该效果的执行实例。
- 未执行等待便完成的 `async DurableTask` 方法仍是普通步骤，其结果由
  父执行实例记录。同步状态转换作为 `DurableTask.RunInline` 效果运行，
  随下一次持久化写入提交，不增加自身的写入操作。
- 重新执行代码中的日志会在重放时重复记录。`logger.ReplaySafe()` 将每个事件记录一次；参阅
  [重放诊断与日志记录](#replay-diagnostics-and-logging)。
- 等待任务体观察的任务，或通过持久化超时等待它们，例如
  `WaitAsync(timeout)`，或配合 `DurableTask.Delay` 使用 `ScheduledTask.WhenAny`。轮询（例如
  `IsCompletedAsync`）仅观察任务在轮询运行时的状态，不记录结果，因此持久化任务体内的
  轮询会抛出 `NotSupportedException`。
- 等待时不要使用 `ConfigureAwait(false)`。任务体在其 Grain 激活实例的调度器上运行，该调度器安排
  持久化操作和状态修改的顺序，并将同步尾部的状态修改与结果一并提交；
  离开该调度器的延续代码会与 Grain 的其他执行轮次发生竞争，并且在那里启动的持久化操作
  （例如 `DurableTask.RunInline`、启动操作及 `DurableTask.GetVersion`）会抛出 `InvalidOperationException`
  （[ORLEANS0226](https://aka.ms/orleans/diagnostics#orleans0226)）。

<a id="recorded-effects"></a>

## 已记录的效果

`DurableTask.RunInline(name, effect)` 在返回之前于调用方当前工作项中运行同步效果，
不挂起，也不创建子执行实例，并记录其结果。任务体后续每次运行（包括重放）
都会返回已记录的结果，或抛出已记录的异常，而不会
再次运行该效果：

```csharp
var summary = DurableTask.RunInline("pay", () => SetStatus(OrderStatus.Paid));
var paymentId = DurableTask.RunInline("payment-id", Guid.NewGuid);
```

- 记录随任务的下一次持久化写入提交，与效果引起的状态修改
  （例如日志化 Grain 状态）一起提交，因此效果的修改与结果一并提交，
  并只生效一次。它不增加写入和等待。如果某次运行在该写入前结束，两者都会丢失，
  下一次运行会再次运行该效果。
- 效果通过其结果向任务体传递数据。它读取后续步骤会修改的状态，
  修改状态，并产生标识符、随机数等值。
- 效果内部的 `DurableTask.RunInline` 调用作为该效果的一部分运行，其结果包含在该效果的记录中。
  启动步骤或子执行实例、Post 操作以及 `DurableTask.GetVersion` 等持久化操作
  应在任务体中运行：在效果内部调用它们会抛出 `InvalidOperationException`，效果将该异常记录为
  自身的结果，因为重放会直接返回记录，而不运行效果
  （[ORLEANS0227](https://aka.ms/orleans/diagnostics#orleans0227)）。
- 失败也会被记录，因为日志化状态不会回滚：抛出异常前的修改
  与已记录的异常一并提交。如果宿主无法存储某个结果（例如结果
  无法序列化），则将该失败记录为结果。
- 同一同步代码块中的连续 `DurableTask.RunInline` 调用，以及它们修改的日志化状态，
  在同一次日志写入中提交，期间不会运行该 Grain 的其他执行轮次。
  效果在任务取消后也会运行；效果返回不表示其记录已提交。
- 名称标识效果的方式与 `WithId` 标识步骤的方式相同：`pay`，随后同一次运行中再次出现时为
  `pay$2`。名称不含 `$`。`DurableTask.RunInline("get-id", Guid.NewGuid)`
  与 `await DurableTask.Run(async () => await ids.NextAsync()).WithId("get-id")` 使用相同的标识符
  `get-id`，但执行方式不同：步骤在被等待时运行，可能位于之后的执行轮次，并作为
  具有自身 `DurableTask.Current` 和取消作用域的子执行实例运行，在
  自身的日志写入中提交。若重放发现记录的是一种操作，而任务体现在运行的是另一种操作，
  则会产生重放分歧，而不会再次运行该工作，因此部署时若将其中一种改为另一种，
  应通过 `DurableTask.GetVersion` 进行分支。
- `DurableTask.RunInline(effect)` 可省略名称以简化代码，例如
  `var paymentId = DurableTask.RunInline(Guid.NewGuid);`。效果使用执行实例的下一个生成标识符，
  如 `$child-1`、`$child-2` 等；未命名的效果与未命名的子执行实例共享这套标识符，
  因此当任务体以相同顺序创建未命名操作时，它在重放时保持稳定。对于在不同运行或部署中
  位置可能发生变化的效果，应为其命名。
- 效果是同步的。异步及持久化效果无法通过编译；若通过泛型代码得到异步结果类型，
  例如 `ValueTask<T>`，则在效果运行前抛出 `NotSupportedException`。
  异步工作作为被等待的步骤运行，例如
  `await DurableTask.Run(async () => ...)` 或 `async DurableTask` 方法。`async void` 委托，
  例如强制转换为 `Action` 的 `async` Lambda 表达式，在首次 `await` 时返回，因此无论效果还是
  步骤都不会记录其后续工作。
- 同步工作应作为效果而不是步骤运行：既不等待也不观察自身
  `CancellationToken` 的步骤会额外创建子执行实例及其独立的日志写入。
- 记录仅在持久化任务内部适用。在任务之外，效果每次都会运行，因此普通方法与
  持久化任务体共享的辅助方法也可以调用它。

<a id="versioned-changes"></a>

## 版本化变更

`DurableTask.GetVersion(name, minSupported, maxSupported)` 返回正在运行的执行实例所遵循的变更版本，
因此在基于旧版代码启动的执行实例仍待完成时，一个任务体可以保留
所有版本的代码：

```csharp
var shipping = DurableTask.GetVersion("carrier-booking", minSupported: 0, maxSupported: 1);
var trackingId = shipping >= 1
    ? await carrier.BookAsync(orderId).WithId("book")
    : await warehouse.ShipAsync(orderId).WithId("ship");
```

- 执行实例中首次检查某一名称时记录版本，后续每次检查及重放
  均返回该版本。首次到达该检查的执行实例记录 `maxSupported`；先前代码已经
  执行过该位置的执行实例记录 `0`。步骤遵循外层执行实例记录的版本。
- `DurableVersion` 与 `DurableSignal<T>` 类似，可一次性声明变更的名称及范围：
  `static readonly DurableVersion CarrierBooking = new("carrier-booking", 0, 1);`，通过
  `CarrierBooking.Get()` 或 `CarrierBooking.IsAtLeast(1)` 读取。
- 提高 `minSupported` 会停用旧版本。记录了范围之外版本的执行实例会
  停滞：宿主以 `DurableTaskAttemptCancellationReason.UnsupportedVersion` 为原因取消尝试，
  其 `DurableTaskAttemptCanceledException` 携带 `DurableTaskVersionException`，宿主丢弃
  尝试的结果，并使任务保持待完成状态，直到支持该版本的代码重放它。
- 在持久化任务之外，检查返回 `maxSupported`。名称不含 `$`，
  [ORLEANS0218](https://aka.ms/orleans/diagnostics#orleans0218) 会报告
  非常量的名称和范围。

<a id="replay-diagnostics-and-logging"></a>

## 重放诊断与日志记录

`DurableTask.Current.IsReplaying` 报告任务体的本次运行是否正在重新执行先前运行记录的
操作。若某次运行发现此前在任务树中记录了操作（例如子执行实例、
分离任务的记录及 `RunInline` 记录），则自该次运行开始即为 `true`，
直到该次运行到达所有这些操作，或者启动先前任何运行都未记录的操作。随后它
保持 `false`。首次运行全程报告 `false`。调用方发送给任务的信号值
不计为已记录的操作。

仅将它用于日志记录和诊断。如果一次运行在日志写入提交其工作之前结束，
它会被重放，因此仅在 `IsReplaying` 为 `false` 时运行的代码可能运行两次或完全不运行；
根据该值分支的代码会在不同运行中调度不同的操作。只应运行一次的工作应放在
`DurableTask.RunInline`、步骤或任务体的同步尾部中。

`logger.ReplaySafe()` 返回一个在任务重放期间跳过事件的日志记录器，使每个事件
记录一次：

```csharp
private readonly ILogger _log = logger.ReplaySafe();

public async DurableTask<OrderResult> PlaceOrderAsync(SubmitOrder request)
{
    _log.LogInformation("Placing order {OrderId}", request.OrderId);
    ...
}
```

日志记录器在记录日志时读取当前运行的任务，因此同一实例可用于持久化任务体和
普通 Grain 方法。`DurableTask.RunInline` 效果内部或已完成步骤内部的日志即使不使用它也
不会重复，因为重放直接返回已记录的结果，而不运行这些操作。

该包包含分析器，会将导致重放执行不同代码路径或未按编写方式运行的代码
报告为 ORLEANS0200 至 ORLEANS0228 诊断。每个诊断均链接到说明
风险及修复方法的页面。

<a id="host-responsibilities"></a>

## 宿主职责

宿主派生自 `DurableTaskContext`，实现 `ISchedulableTask` 定义
和 `IScheduledTaskHandle` 句柄，并持久化 `TaskId` 到定义、
响应及取消状态的映射。重新调度已有 ID 时，宿主返回已记录的响应；
通过记录选择或从持久化完成顺序派生选择，使每次针对同一决策 ID 的 `SelectCompletionAsync` 调用均返回相同选择，
并提供
与重放一致的 `UtcNow` 值。宿主运行及重放已取消任务的任务体时应用持久化
请求及记录的原因，并将已取消的叶节点置为取消终态。宿主实现
`IScheduledTaskHandle.CancelAsync(reason, cancellationToken)`，将首次请求的原因与任务
取消状态一并记录；没有原因的请求传入 `null`。宿主通过
`DurableTaskContext.StartTaskAsync` 启动任务，该方法接收 `DurableTaskStartOptions`：`Detached` 用于启动
将当前执行实例指定为订阅者的独立根任务，`WaitForAcceptance` 用于在任务宿主接受该任务后
才完成的启动操作。启动子执行实例时读取 `DurableTaskContext.SchedulesShieldedChildren`，
将其与子执行实例一起记录；既不因父执行实例之前的取消请求而取消受屏蔽保护的子执行实例，
也不将父执行实例之后的取消请求传递给它。能够同步暂存启动操作的宿主重写
`DurableTaskContext.PostTask`；能够在持久化发件箱中暂存根任务启动操作的定义实现
`ISchedulableTask.Post`。`ISchedulableTask.ScheduleAsync` 在宿主接受并持久化
存储该任务后完成。
支持信号的宿主重写 `DurableTaskContext.WaitForSignalAsync`，针对每个
任务的每个信号名称记录首个值，并投递发送方发出的信号。支持中止的宿主
实现 `IScheduledTaskHandle.AbortAsync`。支持已记录效果的宿主重写
`DurableTaskContext.RunEffect`：返回效果已记录的结果，或运行效果并在同一同步工作中暂存其
记录，使记录与效果所做的状态修改一起提交。将任务体的状态修改与结果一并提交的宿主，在
`DurableTaskContext.OnBodyCompleted` 中暂存结果；
该方法与任务体的最后一段代码同步运行。跟踪重放的宿主
重写 `DurableTaskContext.IsReplaying`。

执行实例的代码在一个调度器上运行，即宿主创建其
`DurableTaskContext` 时的当前调度器，例如 Grain 激活实例的调度器，因此上下文状态无需
同步：宿主在该调度器上通过 `RequestTaskCancellation` 请求取消任务，并通过
`CancelAttempt` 取消执行尝试，调试版本会对此进行断言。上下文拥有尝试的取消状态。
当重放以抽象层能够观察到的方式偏离其记录时，上下文会自行以 `DurableTaskAttemptCancellationReason.Diverged` 取消尝试，
例如代码不再预期某个已记录结果的类型，或已记录的选择不再是候选项。
宿主丢弃 `IsAttemptCanceled` 为 `true` 的尝试的响应，
并按照其 `AttemptCancellationReason` 处理任务。在执行实例已经记录的 ID 下使用相同定义
启动任务，会重新关联已记录的任务；在已记录 ID 下使用另一种
定义则构成重放分歧。
