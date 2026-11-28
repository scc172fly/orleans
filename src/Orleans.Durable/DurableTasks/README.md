<a id="microsoft-orleans-durable-tasks"></a>

# Microsoft Orleans 持久化任务

> [!IMPORTANT]
> 持久化任务包含在尚待首次发布到 NuGet 的预发行 Alpha 包 `Microsoft.Orleans.Durable` 中。
> 在功能稳定之前，其 API 和持久化格式
> 可能发生变化。

`Microsoft.Orleans.Durable` 中的持久化任务运行时通过 Orleans Messaging 执行返回
`Orleans.Durable.Tasks.DurableTask` 的 Grain 接口方法。它负责请求
协议、运行时适配器、存储集成、代码生成映射、
诊断、取消传播、完成消息投递、信号以及持久化延迟；
延迟通过已调度的收件箱消息恢复执行。

<a id="configuration"></a>

## 配置

此仓库中的项目在声明或调用持久化 Grain 接口时，
可以引用源码项目：

```csharp
clientBuilder.AddDurableTasks();

siloBuilder.AddJournaling();
siloBuilder.AddDurableTasks();
```

Silo 注册组合 Messaging，后者使用 Durable Jobs 唤醒具有待处理消息或可运行执行实例的
Grain。生产环境应为 Journaling 配置持久化日志存储
提供程序和 Durable Jobs 提供程序。任务状态可存储于
任何日志格式中，包括
默认 JSON 格式和 Orleans 二进制格式。请求和响应作为
Orleans 序列化后的有效载荷存储，因此它们的类型需要 Orleans 序列化器，
但不需要 JSON 元数据；`AddDurableTasks` 为其余任务状态注册 JSON 元数据。
任务状态位于 Grain 的日志中，该日志是 Grain 各执行实例的
权威数据源；测试使用易失性日志存储将其保留在内存中，
例如 `AddVolatileJournalStorage`。

持久化 Grain 派生自 `DurableGrain`，`AddDurableTasks` 为每个此类 Grain
提供收件箱和发件箱。激活初始化期间，在日志恢复之前，
适配器将运行时绑定到 Grain 的日志和收件箱；没有日志化状态管理器的激活实例会在
此初始化阶段失败。

`GetDurableTask<T>(rootId)` 或其 `TaskId` 重载返回指向现有任务的 `DurableTaskReference<T>`，
而不调度新的执行实例。客户端和普通 Grain 方法可以等待、轮询、
取消或中止它：

```csharp
var task = grain.GetDurableTask<MyResult>("stable-root-id");
var response = await task.GetResponseAsync(cancellationToken);
```

通过 `GetAwaiter` 扩展方法可以等待此引用，因此检查返回类型实例成员的框架
（例如 ASP.NET Core Minimal API）会将其视为一个值：处理程序
直接返回 `grain.GetDurableTask<T>(id)`。`IDurableTaskReference` 无需知道引用的结果类型即可读取其 `Target`
和 `Id`。

`DurableTaskAddress(GrainId Target, TaskId Id)` 是以值表示的任务引用，代码可以将其存储
在日志化状态中，或通过 Grain 调用及 HTTP 有效载荷发送。Orleans 对其进行序列化，
System.Text.Json 将其写为 `{"Target":"{grain type}/{grain key}","Id":"{task ID}"}`，所需元数据
由 `AddDurableTasks` 为 JSON 日志注册。`TaskId` 通过抽象层为其声明的转换器
序列化为转义路径。Grain 和客户端共用的 `IGrainFactory.GetDurableTask<T>(address)`
（客户端使用 `IClusterClient`）将地址转换回引用，
`DurableTaskReference<T>.Address` 从引用中读取地址：

```csharp
_pending.Value = order.GetDurableTask<OrderResult>(orderTaskId).Address;
await WriteStateAsync();

// Later, after any number of reactivations, in a durable task or elsewhere:
var result = await GrainFactory.GetDurableTask<OrderResult>(_pending.Value);
```

`ScheduledTask.TryGetAddress(out address)` 通过句柄的
`IDurableTaskReference` 功能读取其任务地址。客户端和普通 Grain 方法
启动的根任务、`IDurableTaskManager.GetTask` 返回的根任务，以及持久化执行实例中的分离
任务和 `GetDurableTask` 订阅的句柄具有地址。附接子执行实例属于调用方的
执行树，没有地址。

客户端和普通 Grain 方法的等待与轮询会重试不影响任务本身的失败，
例如连接丢失、Silo 停止或崩溃、调用超时或激活实例正在
停用。它们采用从 50 毫秒到 5 秒的指数退避，直到任务所在的 Grain 响应，或
调用方的令牌或等待超时结束该等待。如果根任务不存在或其结果保留期
已结束，则立即抛出 `DurableTaskNotFoundException`。返回的失败响应仍是执行
失败，即使存储的用户异常恰好具有相同类型也是如此。

持久化任务可等待同一引用，以订阅自己未调用的已调用任务的结果，
例如客户端调度、由 Webhook 中继报告的操作：

```csharp
var result = await operation.GetDurableTask<OperationResult>("operation-42")
    .WaitAsync(TimeSpan.FromDays(1));
```

- 等待引用、调度引用，或将引用传给 `WhenAny`、`Race`
  或 `WaitAsync` 等组合器，会为当前执行实例的子执行实例建立订阅，并记录在该操作分配的
  子 ID 下。子执行实例的指纹 `$subscribe:{target}{taskId}` 标识任务；
  子执行实例及其订阅在一次日志写入中提交，因此重放到达该子执行实例时
  不会再次订阅，而是观察已记录的结果。
- 订阅是一条 `durable-rpc/invoke` 消息，携带子执行实例的 ID 而非
  请求：目标既不调度也不认领任务，不记录也不验证调用方。
  它将 `(subscriber grain, child ID)` 添加到任务的完成消息目的地，与调用方并列；
  调用方以 `(caller, task ID)` 订阅。任务结果以相同方式到达每个目的地：
  通过发件箱投递，并保留任务直至完成消息退出发件箱。子执行实例以
  该任务的结果、失败或取消完成。
- 对终态任务的订阅立即发送其完成消息；若目标
  未将该任务保留为已调用任务，则立即以 `DurableTaskNotFoundException` 完成
  子执行实例。订阅者获得的任务 ID 指向目标已持有的任务：
  `ScheduleAsync` 在目标接受任务后返回，而启动分离任务的持久化执行实例
  通过启动操作本身完成订阅。
- 取消子执行实例会将其置为取消终态，并发送取消订阅请求，即命名该子执行实例的 `durable-rpc/cancel`
  消息，在任务仍运行时移除目的地；任务本身
  继续运行。中止或删除子执行实例的树会以相同方式结束它，之后
  到达已结束子执行实例的完成消息不改变任何内容。
- 订阅同一 Grain 上的任务会在任务状态中添加目的地，存储
  任务结果的写入也会完成子执行实例。
- 在持久化执行实例之外，引用的轮询及控制成员，以及对它的等待，均会调用
  目标；持久化执行实例不会记录这些调用。

持久化 Grain 通过 `IDurableTaskManager` 观察、取消及中止自身任务，
并从其激活实例的服务中注入该管理器。管理器直接操作激活实例的
运行时，无需 Grain 调用自身。接受根 ID 字符串的重载
寻址根任务 `TaskId.CreateRoot(rootId)`：

```csharp
public sealed class OrderGrain(IDurableTaskManager tasks) : DurableGrain, IOrderGrain
{
    public async Task<bool> CancelAsync()
    {
        await tasks.CancelAsync(PlaceOrderTaskId(this.GetPrimaryKey()), "Cancelled by the buyer");
        return true;
    }
}
```

取消请求可携带原因，被取消的工作流从
`DurableTask.Current.TaskCancellationReason` 读取该原因，例如记录停止的原因。首次
请求决定原因；`ScheduledTask.CancelAsync(reason)`、
`DurableTaskReference<T>.CancelAsync(reason)` 和 `IDurableTaskManager.CancelAsync(taskId, reason)`
可从客户端、工具或任务所在的 Grain 传递原因。

持久化 Grain 调用上的配置决定任务的性质，启动任务的调用方式决定
调用方何时继续执行：

```csharp
// In a workflow: start the order as an independent root task, in the same write as the basket's change.
_ = order.PlaceOrderAsync(request).Detached(PlaceOrderTaskId(orderId)).Post();

// Anywhere: return once the target holds the task, so its ID can be handed to other parties.
var placement = await order.PlaceOrderAsync(request).WithId(PlaceOrderTaskId(orderId)).ScheduleAsync();
```

- 在持久化任务中，等待、调度或 Post 调用会在任务 ID 下记录附接子执行实例，
  并暂存一条 `durable-rpc/invoke` 消息，其目标为调用方认领任务。
  根 ID 的作用域为单个 Grain，因此目标在调用方的作用域下存储子执行实例，
  即 `$caller/{callerGrainId}/{incarnation}/{taskId}`：即使两个调用方的工作流共享根 ID 及
  子任务名称，也会在同一目标上运行不同任务。消息通过子执行实例的*传输 ID*
  `{incarnation}/{taskId}` 命名它，其中 `{incarnation}` 是调用方本次运行的世代标识，即
  调用方 Grain 存储根任务调用请求时生成、以 `"N"` 格式表示的 GUID。
  Grain 删除根 ID 的先前运行后，再次运行该根 ID 会拥有新的世代标识，因此它的调用
  到达新的任务，其消息不会与先前运行的消息去重。调用方的
  调用、取消及中止使用子执行实例的传输 ID 寻址，目标将其映射到调用方作用域，
  目标的完成消息也将该传输 ID 作为订阅者；调用方忽略属于
  先前运行的完成消息。订阅和分离启动也使用传输 ID 命名记录它们的子执行实例。
  分离启动发送的根 ID 直接寻址目标的根任务；根 ID
  `$caller` 为保留名称：只有其他 Grain 的调用才会在它之下创建任务。
  目标在日志写入存储任务后确认调用，启动方发件箱在记录子执行实例已获接纳的
  写入中将该调用以已投递状态退出发件箱。
  `ScheduleAsync` 在接纳或任务结果持久化后返回。`Post` 在调用代码的执行轮次内暂存
  子执行实例及其调用，使它们随 Grain 的下一次日志
  写入提交；重放到达该子执行实例时不会重新发送。调用投递后目标才持有 Post 的任务，
  因此将任务 ID 交给其他参与方的代码应使用
  `ScheduleAsync` 启动任务。
- 分离调用是其目标上的独立根任务。此宿主支持分离持久化 Grain 调用：
  目标从调用的序列化请求恢复根任务，因此分离其他
  `ISchedulableTask` 会在记录或发送任何内容前使操作以 `NotSupportedException` 失败。
  执行实例将其记录在
  `{taskId}/$detach:{target}:{rootId}` 下，其中记录根 ID 及请求的方法，因此
  重放到达该记录时不会重新启动任务，无论请求参数为何；
  若在同一记录下启动另一方法，则发生执行偏离。调用携带请求并将该记录指定
  为订阅者，目标存储任务时将该记录加入任务的完成消息
  目的地。
  该记录像 `GetDurableTask` 订阅一样观察任务：任务的结果完成该记录，
  取消该记录、中止或删除其树，或取消启动方，都只会结束
  订阅。其根 ID 是 `.Detached(rootId)` 的原始名称，或单个路径段
  `{grainId}/{taskId}/{segment}`，其中各部分像任务 ID 一样转义 `/` 和 `\`，`{grainId}`
  是启动方 Grain，`{segment}` 是操作的路径段，因此不同 Grain 上具有相同任务 ID 的工作流
  会派生出不同根 ID。
- 句柄的 `TargetTaskId` 命名其记录启动或观察的、位于存储它的 Grain 上的任务：
  分离任务的根 ID、`GetDurableTask` 订阅所订阅的任务，
  或对 Grain 的调用所使用的 `$caller/{grainId}/{incarnation}/{taskId}`。其他参与方据此寻址任务，例如
  客户端使用 `GetDurableTask<T>(handle.TargetTaskId)` 轮询分离任务。句柄的 `Id`
  命名记录，供组合器、取消及重放寻址。
- 对其他 Grain 上任务的每次调用，发件箱均按引用持有；启动方从按消息 ID 建键的
  日志化待处理调用表提供它，直到发件箱使其退出。
  此表独立于执行树，因此启动请求在创建它的执行树被删除后仍存在，
  调用的有效载荷只写入日志一次。重放到达调用仍处于待处理状态的启动操作时，
  会与该调用合并。
- 仅当任务 ID 已标识另一请求或调用方时，目标才会立即拒绝调用，
  且此拒绝为最终结果：它使用 `DurableTaskRejectionCodes` 中的 `durable-rpc/conflict` 代码
  及拒绝原因将调用转为死信，启动方发件箱以被拒绝状态使该调用退出，
  并携带两者。若目标因其他原因（例如
  暂时性故障或无法解码的请求）未能接受调用，则抛出异常，启动方的发件箱会重试
  投递直至 Messaging 的 `OutboxRetryTimeout`；滚动部署期间，重试可能到达能够解码请求的
  激活实例。启动方记录将失败存储为自身结果，并且
  `ScheduleAsync` 以及等待任务或其句柄时，在每次重放中均会抛出该失败：

  | 结果 | 代码 | 失败异常 |
  | --- | --- | --- |
  | 任务 ID 已标识另一请求或调用方。 | `durable-rpc/conflict` | `DurableTaskConflictException` |
  | 调用在目标接纳窗口之后到达，即自启动方首次暂存调用起经过 Messaging 的 `DeduplicationWindow` 后。目标可能先前已运行并删除任务，因此结果未知。 | `durable-rpc/expired` | `DurableTaskRejectedException` |
  | 目标使用其他代码或不带代码拒绝调用。 | 目标的代码 | `DurableTaskRejectedException` |
  | 发件箱在 `OutboxRetryTimeout` 前无法确认调用（例如始终无法解码），或目标在接纳截止时间之后阻止该调用。先前尝试可能已到达目标，因此结果未知。 | | `DurableTaskDeliveryException`，携带死信原因 |

  结果未知意味着任务可能已经运行：在使用另一任务 ID 再次启动工作前，应先确定它是否已运行，
  例如轮询任务或检查其效果。

  `DurableTaskRejectedException.Code` 报告目标的代码，`Target` 报告拒绝调用的 Grain。
- 在持久化 Grain 的普通方法中，`ScheduleAsync` 像客户端一样直接调用目标，
  在目标已存储任务后返回；对该 Grain 自身的调用通过 Grain 自己的
  运行时进行。`Post` 将调用暂存在 Grain 的发件箱中，使调用随 Grain 的下一次
  日志写入与该方法所做的状态修改一起提交，并投递一次。
  启动的任务是根任务，该 Grain 不存储它，其目标不向任何
  Grain 发送结果。
- 客户端的 `ScheduleAsync` 在目标存储任务后返回。客户端没有发件箱，
  因此 `Post` 抛出 `InvalidOperationException`。
- 对 Grain 自身的调用在提交该调用的日志写入后作为本地根任务启动。
  其目标像保留客户端调度的根任务一样保留它，通过 `GetDurableTask<T>(rootId)` 轮询。

`DurableSignal<T>` 将外部事件（例如发货通知或人工
审批）传递到已调用的任务中。任务执行实例使用 `WaitAsync()` 等待，发送方将
值投递至 `(grain, task ID, signal name)`：

```csharp
static readonly DurableSignal<Shipment> Shipped = new("shipped");

// In the workflow, which a client or another grain invoked under its task ID.
var shipment = await Shipped.WaitAsync();

// From a client, or from a durable task on another grain.
await order.SignalDurableTaskAsync(workflowId, Shipped, shipment);

// From the grain's own methods.
await tasks.SignalAsync(workflowId, Shipped, shipment);
```

- 已调用任务在其执行树中为每个名称记录第一个值，作为终态记录
  `{taskId}/$signal:{name}`，与投递该值的变更一起提交。
  树中对此名称的每次等待（含重放期间的等待）都返回记录的
  值，删除树时也删除该记录。对应等待之前到达的信号
  会使等待立即完成。
- 重复发送具有相等序列化值的信号会成功，且不改变任务。
  不同值的信号、针对没有该名称值的终态任务的信号，以及
  针对 Grain 未保留为已调用任务的信号，均会被拒绝：
  调用方收到 `InvalidOperationException` 或 `DurableTaskNotFoundException`，收件箱
  将被拒绝的信号消息转为死信，并以拒绝原因为死信原因。
- 发送方可以在一次写入中提交调用及其信号；发件箱独立投递
  两者，因此信号消息可能先到达。收件箱会为 Grain 尚未持有的任务记录信号消息，
  调用到达时即可找到该值。
  Grain 自发送方首次暂存调用起，在 Messaging 的 `DeduplicationWindow` 内接纳调用；
  信号值到达后保留该窗口长度的两倍，
  以覆盖发送方与 Grain 时钟之间的偏差，使获接纳的调用
  能找到该值。如果在此期间没有调用到达，Grain 删除该值并记录
  警告。
- 在持久化任务中，发送操作和执行树中的标记在一次日志写入中提交，
  与 Post 启动类似，因此重放到达该标记时不会重新发送，无论传入何种值。
  标记为
  `{taskId}/$send:{target}:{task}:{name}`，在同一执行实例中重复发送某个信号时，
  例如 `TaskNotFound` 后重试，按 `$send:...$2` 等编号，使每次都会
  重新发送，目标将相等值报告为 `Duplicate`。发送给
  另一个 Grain 的信号是发件箱中的 `durable-rpc/signal` 消息，其标识包含
  发送方、目标、任务、名称、值及发送方本次运行的世代标识；目标收件箱将值直接记录在任务
  状态中。发给 Grain 自身的信号在同一次写入中记录。
- 从客户端或普通 Grain 方法调用 `SignalDurableTaskAsync` 会调用目标，
  在任务已持久化持有该值后完成。Grain 通过
  `IDurableTaskManager.SignalAsync` 向自身任务发送信号，该操作随 Grain 的待提交状态修改一起提交。
- `IDurableTaskManager.TrySignalAsync`，以及用于客户端和普通 Grain 方法的
  `TrySignalDurableTaskAsync`，返回 `DurableSignalResult` 而不是抛出异常：任务已持久化持有值时返回 `Accepted` 或
  `Duplicate`，信号被拒绝时返回 `Conflict`、`TaskNotFound` 或
  `TaskCompleted`，拒绝不会改变任何内容。在持久化任务中，
  管理器将结果与发送操作一并记录，因此重放返回相同结果：

```csharp
var result = await tasks.TrySignalAsync(PlaceOrderTaskId(orderId), Shipped, shipment);
if (result is DurableSignalResult.Conflict or DurableSignalResult.TaskCompleted)
{
    return false; // Already shipped, or the order finished.
}
```
- 等待会将其执行实例的逻辑时间推进到信号被记录的时刻。使用
  `TryWaitAsync(deadline)` 设置截止时间，即让等待与 `DurableTask.DelayUntil` 通过 `Race` 竞速；
  其索引槽属于等待方执行实例的树。竞速取消
  未胜出的输入，并在两者均达到终态后完成。

<a id="diagnostics"></a>

## 诊断

`DurableTaskReference<T>.GetStatusAsync` 和 `GetResponseAsync` 报告任务的状态和结果。
`DurableTaskEvents` 是 `Orleans.DurableTasks` 诊断侦听器，
在运行时暂存状态转换时，于执行该转换的激活实例上同步报告：`TaskCreated`、
`TaskCompleted`、`TaskDeleted`、`ExecutionStarted`、`ExecutionStopped`、`ReplayEnded`、
`SignalWaitStarted`、`SignalWaitEnded` 和 `SelectionWaitStarted`。每个事件携带激活实例的
`IGrainContext` 及任务 ID。测试以事件作为同步屏障，而非轮询。

<a id="runtime-guarantees"></a>

## 运行时保证

- `(target GrainId, TaskId)` 标识一个执行实例。请求的标识由其
  调用方及所调用的接口方法确定，方法通过其
  生成的可调用类型标识；不比较参数。相同调用方使用相同方法
  再次调度同一任务 ID，会访问现有任务，即使
  参数不同或序列化结果不同，例如部署
  新增可选字段之后。使用另一方法或调用方调度会抛出
  `DurableTaskConflictException`，它继承自 `InvalidOperationException`，并使
  现有任务保持不变。每个任务还存储其请求
  序列化参数的指纹，诊断会报告该指纹，但不会对其进行比较。
- 请求标识、任务状态、调用方订阅、收件箱完成记录及
  恢复所有权在执行开始前提交。
- 任务状态及传出消息共享 Grain 的 Journaling 提交边界，
  包括 Grain 发送给自身的消息，这些消息由其自身收件箱接受。
  操作在暂存前验证，且仅暂存可以安全
  提交的修改；日志写入失败会停用 Grain，下一激活实例
  从已提交状态恢复。
- 收件箱消息同步暂存，使收件箱将每条消息
  与它引起的修改一起提交。调用、完成消息、取消及信号
  在投递时被接受；已调度的延迟恢复在
  到期时处理。执行启动、本地完成及取消传播发生于
  提交之后。
- 激活实例在日志、收件箱和发件箱恢复后恢复任务状态，
  并在 Grain 的
  `OnActivateAsync` 完成后启动已恢复的执行实例。
- 执行实例的一次尝试分段运行，即各持久化等待之间的代码。
  每个执行段都是激活实例上的本地请求，遵循 Grain 的
  可重入性规则；在不可重入 Grain 上，执行段像 Grain 方法一样排斥该 Grain 的
  其他请求。尚未完成的持久化等待，例如
  等待子执行实例的结果、信号、延迟或多个完成消息中的第一个，
  会挂起尝试并结束其执行段；等待完成时，
  新的执行段恢复等待中的代码。其他等待，例如日志写入
  和对其他 Grain 的调用，保留在原执行段内。
- 等待自身 Grain 上任务的请求（例如 Grain 方法
  等待 `this.AsReference<T>().M()` 或 `IDurableTaskManager.GetTask(id)` 的
  响应）必须允许该任务的各执行段与自身交错推进：为接口
  方法标记 `[AlwaysInterleave]`，或为 Grain 类标记 `[Reentrant]`，也可以从持久化代码中等待任务，
  其持久化等待会结束自身执行段。否则，不可重入的
  请求在等待时会阻挡各执行段，直到调用
  超时。持有 Grain 排他区段的代码（例如事务）
  会阻挡执行段，直到退出该区段，即使是
  `[AlwaysInterleave]` 方法也如此，因此这类代码仅在区段之外
  等待其 Grain 的任务。
- 运行时向收件箱注册唤醒延迟：只要 Grain 持有可运行的
  执行实例（其执行段正在运行或即将运行），Messaging 就保留该 Grain 的
  Durable Jobs 唤醒；日志写入在成为持久化状态之前确保该唤醒已安排。
  Grain 在有可运行执行实例时崩溃，会在约 30 秒内重新激活，
  恢复过程继续该执行实例。激活实例
  启动时，其恢复的待完成执行实例重新启用唤醒；
  停用期间，待完成执行实例仍保留唤醒，因此
  停用时丢弃的可运行执行实例会在下一
  激活实例中恢复。
  被挂起的执行实例在其等待的消息重新激活 Grain 时恢复。
- 激活实例的关闭在开始停用时启动，早于 Grain 的
  `OnDeactivateAsync`。它阻止新的执行，并以
  `ActivationStopping` 为原因取消每个运行中的尝试：从此，该尝试的
  持久化操作抛出 `DurableTaskAttemptCanceledException`，其 `AttemptCancellationToken` 及
  `CancellationToken` 被取消。被挂起的尝试直接丢弃，不再运行
  后续代码；运行中的执行段会结束，其结果被丢弃。
  下一激活实例重放已提交的工作。其他 Grain 工作，例如
  `OnDeactivateAsync` 和收件箱，在执行段结束期间继续提交，
  被取消执行段的清理代码所做的日志化状态修改也随之
  提交。在该激活实例上的轮询或等待
  会以 `DurableTaskAttemptCanceledException` 失败，与运行时停止期间到达的调用一样；
  任务将在下一
  激活实例中继续。
- 每个请求及结果的有效载荷在每个 Grain 上仅写入日志一次。被调用方的收件箱
  接受调用，调用方的收件箱接受完成消息，并直接写入
  任务状态，仅将消息的已处理记录与之一起写入日志。
  被调用方的发件箱仅记录每个完成消息的 ID 和投递状态，
  运行时在每次投递尝试时从存储的结果构造消息。
  任务状态将
  请求和结果的有效载荷与任务元数据分开持久化，因此更新
  元数据不会重写它们。转为死信的完成消息是例外：
  其死信持有一份结果副本以供检查和重放，同时
  任务状态保留自身的结果，期限为 `ResultRetentionPeriod`，因此轮询方或
  重新调用仍能观察结果。
- 终态响应与完成通知一同提交。被调用方
  保留结果，直至完成消息退出其发件箱。
- 任务的完成消息目的地就是其订阅者：调用方、通过
  `GetDurableTask` 订阅的各持久化执行实例的子执行实例，以及各以分离方式
  启动任务的执行实例的记录。每个订阅者都通过与结果
  一起提交的完成消息收到结果，并且任务保留到每条
  完成消息退出发件箱为止：有 Grain 调用方的任务在最后一条退出时删除，
  无 Grain 调用方的任务在完成后将结果保留 `ResultRetentionPeriod`，
  且至少保留至其订阅者的完成消息全部退出。转为死信的完成消息
  会重新开始该保留期。订阅者的 Grain 将完成消息直接接受到
  子执行实例中；对于已结束或随其树删除的子执行实例，完成消息被
  接受但不产生修改。
- 已调用任务（由客户端或其他 Grain 作为请求调度）拥有
  执行树：包含任务及本身并非已调用任务的后代，
  例如本地子执行实例、延迟、组合器决策、订阅、
  分离任务的记录及对其他 Grain 的调用。调用方选择任务标识，因此嵌套于
  另一任务标识下的已调用任务仍保持自己的生命周期。已调用任务达到
  终态后，Grain 删除其执行树：若调用方 Grain 已持久化接受完成消息，
  则在使完成消息退出发件箱的日志写入中删除；若没有
  Grain 调用方（例如客户端调度的任务），则在完成后经过 `DurableTaskOptions.ResultRetentionPeriod` 时删除；
  若完成消息转为
  死信，则在死信产生后经过同一保留期删除。因而任务标识
  在任务完成后 `ResultRetentionPeriod` 内对调度去重，
  删除后再次调度会作为具有自身世代标识的新运行重新执行请求，
  与 Messaging 的 `DeduplicationWindow` 无关。外部
  客户端轮询保留的结果，删除后的轮询会抛出
  `DurableTaskNotFoundException`。Grain 在激活时、
  每次日志写入后，以及响应轮询、客户端调度或
  订阅前应用保留策略，因此这些操作不会观察到保留期已结束的任务。轮询与 Grain 的其他
  请求及消息交错执行，所以等待中的客户端不会阻碍
  完成任务的工作。
- 删除执行树会取消其未完成的任务：删除的日志
  写入为每个尚待完成的跨 Grain 调用发送取消请求，每个
  运行中的执行实例在该写入之后收到信号，其结果被丢弃。
  已删除执行实例的后续调度会抛出 `OperationCanceledException`。
  针对已删除任务或再次运行的根 ID 的先前运行所到达的完成消息和延迟恢复消息
  均被忽略。调用方发往另一 Grain 上子执行实例的取消请求若早于该子执行实例的调用
  到达，就会被记录，使该调用
  到达时即置为取消终态，该记录随其树删除。
  从客户端、Grain 方法或 `IDurableTaskManager.CancelAsync` 直接取消 Grain 未保留的任务时，
  会抛出 `DurableTaskNotFoundException`，不存储任何内容。
  Grain 自发送方首次暂存调用的时刻（即投递的 `InboxMessage.SentAt`）起，
  在 Messaging 的 `DeduplicationWindow` 内接纳调用，将更晚到达的调用以 `durable-rpc/expired` 代码转为死信。
  提前到达的记录自到达后保留该窗口长度的两倍，因为接纳依据发送方的时钟，而
  记录的生命周期依据 Grain 时钟；Messaging 将发送时间超前于 Grain 时钟的程度限制为一个窗口。
- 如果完成消息存储的结果无法再解码（例如之后的
  部署移除了结果类型），则不能构造该消息。发件箱
  将该完成消息转为死信，并记录解码错误作为原因，
  运行时像处理其他转为死信的完成消息一样使其退出发件箱，无需解码结果。
  激活过程不解码已存储的结果，因此 Grain 继续处理其他
  工作；读取该结果的操作（例如轮询）会将解码
  错误报告给调用方。
- 已存储的请求无法再解码时（例如部署移除了
  其参数包含的某种类型），仅隔离该请求的任务。激活过程根据任务的
  记录种类及是否存储请求对任务分类，无需解码请求，因此 Grain
  可以激活并运行其他任务。启动任务时解码其请求；解码失败时，
  任务保持待完成状态，已存储请求不变，并进入停滞状态：运行时记录
  警告，每个激活实例只触发一次 `ExecutionStalled`，携带 `DurableTaskAttemptCancellationReason.Diverged`
  及解码错误，并保留 Grain 的
  `StalledExecutionRetryInterval` 唤醒，使之后能够解码该请求的激活实例
  运行任务。轮询报告任务为待完成状态，中止该任务可以结束它。
- 无法序列化的结果或失败（例如某个没有编解码器的类型的值或异常）
  无法存储。任务转而以 `SerializerException` 失败，
  其中标明类型及序列化错误，失败像任何结果一样提交，
  因此任务结束，Grain 继续提交其他工作。
- 取消是协作式、单调且幂等的。取消请求先提交，再
  传播：它取消任务树中每个运行中执行实例的持久化令牌，暂存
  对每个待完成跨 Grain 调用的取消消息，被调用方按照相同规则处理其自身
  任务，并将延迟以及任务体尚未启动的任务置为取消终态。已启动的任务体
  决定任务结果：取消从任务体向外传播时，任务被取消；否则
  以任务体的结果或失败完成。任务体在请求之后启动且未受屏蔽保护的附接子执行实例
  不会运行，直接取消；自此类启动操作起，任务体通过
  `IsTaskCancellationRequested` 观察请求，即使请求对应的写入尚未
  完成。任务体在取消点观察取消：即其某个
  持久化操作的开始，例如启动子执行实例、`RunInline` 效果或等待任务或
  信号。运行中的任务体在当前位置观察取消；对于挂起于持久化
  等待的任务体，该位置就是等待点，执行实例在随后日志写入中将该点记录在
  `{execution}/$cancellation-observed` 下。已取消任务体的重放在到达记录的点之前
  使其持久化令牌保持未取消状态，并在该点观察取消，因此已完成的
  子执行实例返回其记录的结果，任务体在原先首次补偿的位置执行补偿。如果重放
  未找到已记录的点，则在到达先前尝试所记录的最后一个操作时
  观察取消，并记录该点。如果重放在到达已记录点之前结束，则发生
  执行偏离并停滞，直到能到达该点的代码重放它。竞态中首个持久化的终态
  结果胜出。
- 任务体使用 `.Shielded()` 启动补偿。受屏蔽保护的子执行实例（例如本地步骤、延迟或
  对其他 Grain 的调用）记录其祖先的取消不传递给它：即使父执行实例已收到取消请求，
  它仍被创建并运行；之后的请求跳过它及其
  后代，恢复时使用它自身的取消状态重放。受屏蔽保护的组合器屏蔽
  其启动的每个输入。作为任务体一部分运行的受屏蔽保护的信号等待，使用
  尝试的 `AttemptCancellationToken` 进行等待，因此任务体的取消不会结束它；受屏蔽保护的
  订阅（包括分离任务的记录）也不会因此取消订阅。该操作自身的
  取消，以及停用、中止其树或
  删除其树引起的尝试取消，仍会传递给每个受屏蔽保护的操作。任务体将必须比任务本身存活更久的
  工作分离出去。
- 取消请求的原因在所到达的每个任务上随该请求一起提交，并随
  取消消息传递到每个待完成的跨 Grain 调用；已取消任务体的重放从
  `DurableTask.Current.TaskCancellationReason` 读取该原因。
- 通过 `IDurableTaskManager.AbortAsync`、`DurableTaskReference<T>.AbortAsync`
  或 `ScheduledTask.AbortAsync` 进行管理性中止，会在一次日志写入中将任务及执行树以
  `DurableTaskAbortedException` 作为失败提交，并在同一次写入中为每个待完成的跨
  Grain 调用暂存中止消息；被调用方也会中止其任务树。树中每个运行中的尝试均以
  `TaskAborted` 为原因取消，结果被丢弃，因此后续持久化操作会抛出异常，
  中止不执行补偿。树中的延迟随树一并达到终态，因此其
  恢复消息成为过期消息。所存储异常的消息为中止原因，任务的完成时间
  记录其中止时刻；轮询任务报告 `Failed` 状态，并带有
  携带中止原因的 `DurableTaskAbortedException`。中止终态任务不会改变
  任何内容；调用之前到达的中止请求作为取消记录。
- 分离任务是其目标上的无调用方根任务。启动方的持久化取消和中止
  不会传递给它，删除启动方的执行树只会删除观察该任务的
  记录。使用相同根 ID 再次启动相同方法不会改变任务。
  在该 ID 下启动另一方法像其他任务 ID 复用冲突一样失败：目标将调用转为死信，
  启动方的记录以该拒绝失败。普通 Grain 方法 Post 启动的分离任务
  没有此记录，因此目标的死信会保留被拒绝的调用。
- 延迟持久化其逻辑到期时间和世代。每个延迟向自身 Grain 发送
  `durable-rpc/resume` 消息，将 `Envelope.DeliverAt` 设置为
  到期时间。Grain 自身的收件箱在延迟对应的日志
  写入中接受该消息，无需经发件箱往返，因此延迟及其恢复消息
  在同一次写入中提交。收件箱在 Durable Jobs 时钟
  到达到期时间时处理恢复消息。逻辑时间、到期时间和任务时间戳均读取该时钟，
  即使用 `DurableJobTimeProviderNames.DurableJobs` 作为键的 `TimeProvider`，因此它们与 Messaging 的调度
  共享同一时间域。恢复消息受延迟世代和其运行世代标识约束，两者均由恢复消息携带：已取消延迟的恢复消息
  将其置为取消终态，重复及过期的恢复消息不会产生影响。
  与其他已调度消息一样，恢复消息不计入 Messaging 的
  `MaxCapacity`：发送方限制其数量，Grain 为自己调度的每个延迟保留一条恢复消息，
  直至该延迟到期。
  延迟的到期时间是一项承诺：如果重放请求另一到期时间，
  例如配置的时长改变之后，则保留记录的时间并
  记录警告。延迟在其写入进行期间被取消时，
  在该写入完成时置为取消终态。
- 消息使用从发送方、接收方、任务及主题派生的稳定标识，
  因此重放的发送操作合并，重复投递被去重。
  消息的发送方用于路由完成消息，并检测与任务记录的
  调用方或被调用方不匹配的消息。它不是授权机制：
  与普通 Grain 方法一样，持久化任务、其信号及取消操作
  可供集群中所有客户端和 Grain 使用，而集群正是 Orleans 的信任
  边界。
- 本地步骤的记录随其首次持久化写入提交：即其结果，或
  它调度的子执行实例、延迟或消息。同步步骤因此需要一次
  日志写入。步骤的标识和输入由父执行实例的输入及
  先前结果决定，因此未找到记录的重放会再次运行步骤。
- 任务体的结果在运行其最后一段代码的工作项中暂存，
  使它与该代码修改的日志化状态在同一次日志写入中提交，
  无论哪个操作请求该写入。返回记录结果的重放
  因此会发现这些修改仅应用一次。
- `DurableTask.RunInline` 效果在激活实例上运行，其结果被记录为
  执行树中的终态条目 `{taskId}/{name}`，与效果所修改的日志化状态在
  同一工作项中暂存。两者在下一次
  日志写入中提交，重放返回记录的结果，而不运行
  效果。该记录随树删除；激活实例若在
  此次写入之前结束，两者均会丢失，因此下一次尝试再次运行效果。
  无法序列化的结果会被记录为该失败。离开激活实例调度器后继续执行的代码，
  例如 `ConfigureAwait(false)` 之后的代码，无法记录效果。
  效果和子执行实例共享标识符：`DurableTask.RunInline("x", f)` 在
  `x` 下记录，如同 `WithId("x")` 命名子执行实例；未命名的效果
  `DurableTask.RunInline(effect)` 使用执行实例的下一个自动生成的子
  标识符，例如 `$child-3`。重放在运行效果处发现子执行实例，
  或在启动子执行实例处发现效果，会发生执行偏离，且偏离信息标明两种
  类型；具名组合器的输入位于某效果的标识符之下，或
  效果出现在具名组合器记录输入的位置，也会产生偏离。
- 重放若偏离执行实例记录，例如在已记录标识符下请求其他操作，
  或发现已记录结果无法再解码，则进入停滞状态：
  运行时以
  `DurableTaskAttemptCancellationReason.Diverged` 取消尝试，与对不受支持版本使用 `UnsupportedVersion`
  取消一样；它丢弃结果，使任务保持待完成状态，并触发
  `ExecutionStalled`，携带原因及偏离信息。操作标识由其
  记录类型、标识符及目标确定：调用的 Grain 和方法、分离
  启动的根 ID 和方法，或订阅的任务。不比较参数，因此
  调用传入其他参数或序列化结果不同参数的重放，
  会读取该调用的记录结果。每条记录存储创建它的
  操作类型，例如调用、步骤、延迟、效果或在调用之前
  到达的取消请求，因此在记录为步骤的位置请求延迟、
  在记录为延迟的位置请求步骤，或在记录为调用的位置请求延迟的重放，都会停滞，
  不观察也不修改其他操作的记录。Grain 每隔
  `DurableTaskOptions.StalledExecutionRetryInterval` 唤醒一次，使之后的激活实例使用它运行的代码重放
  执行实例。若调用方的启动与同一根标识符下的另一方法冲突，
  调用方仍会收到 `DurableTaskConflictException`。
- `DurableTask.GetVersion(name, minSupported, maxSupported)` 将执行实例遵循的
  变更版本记录为其树中的终态条目
  `{taskId}/$version:{name}`，在执行实例首次检查名称时创建，并像效果记录一样
  暂存。首次到达该检查的执行实例记录 `maxSupported`，
  若重放到达检查时仍有先前运行记录的操作尚未到达，则记录 `0`，
  因为先前代码已经越过该检查。步骤读取同一个已调用任务内的
  外层执行实例记录。版本记录不属于重放需要到达的
  操作，因此删除检查的部署不会改变
  重放进度。若尝试发现版本超出受支持
  范围，则停滞：运行时以 `UnsupportedVersion` 取消尝试，与
  取消正在停止的激活实例的尝试一样，因此该检查及尝试后续的
  持久化操作抛出 `DurableTaskAttemptCanceledException`，其内部异常是
  `DurableTaskVersionException`。尝试结果被丢弃，任务保持
  待完成状态，Grain 保留一分钟唤醒，使后续激活实例能够重放。
- 持久化任务将进度发布到其 Grain 的 `IDurableLog<T>`。它通过
  `DurableTask.RunInline(() => log.Append(item))` 追加条目，条目与
  效果记录一并提交。以流式方式输出内容的步骤将内容写入通过
  `BeginDraft()` 获取的草稿；传入 `includeDrafts: true` 的读取方可实时收到内容，
  步骤在其最终同步代码中提交草稿，使条目与
  步骤结果在同一次日志写入中提交。无论哪种方式，重放均返回
  记录的序列号，而不再次追加或流式输出。
- 任务体的某次运行在开始时，只要先前运行在任务树中
  记录了操作，就报告 `IsReplaying`：包括子执行实例（含对其他
  任务的订阅）、分离及信号发送标记和已记录效果，但不包括信号值或
  已调用的后代任务。
  当该运行到达所有这些操作，或启动先前任何运行均未记录的操作时，
  停止报告重放；重复到达某个操作不会使重放状态结束。
  该状态仅用于日志记录及诊断。
- 恢复先于新的执行。子执行实例标识符、选择决策及
  出站持久化消息在重放中保持稳定。若重放在已记录标识符下调度其他
  操作，则以错误失败，错误中标明
  已记录及请求的操作。每个执行实例的逻辑 UTC
  时间在其效果提交前持久化，并在重放中复用；该时间
  推进至执行实例观察到的每个子执行实例结果的记录完成时间，
  使延迟从其前置工作完成的时刻开始计时。
- 每个终态结果携带完成序列号，Grain 在暂存结果时于同一次日志写入中
  标记它：包括本地子执行实例的结果、调用方收件箱接受的调用结果、
  处理恢复消息时的延迟结果，以及
  取消和中止。之后暂存的结果获得更大的序列号。
  序列号只为任务状态增加一个整数，不增加日志条目：激活时从
  已恢复的任务派生最后一个序列号。`WhenAny` 和 `WhenEach` 选择
  序列号最小的已完成候选项，因此重放选择相同的
  候选项，无需记录决策，即使多个结果在同一次
  写入中提交也是如此。对于没有序列号便完成的候选项，例如已取消祖先的
  启动被拒绝的子执行实例，或在开始标记序列号前已存储的结果，
  通过记录在执行树中的决策进行选择，已记录的决策保留其胜出者。

持久化保证涵盖参与 Orleans Journaling 和
Messaging 提交的状态与消息。直接外部副作用需要自身的幂等性
保证。
