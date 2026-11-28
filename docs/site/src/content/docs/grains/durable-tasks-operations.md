---
title: 持久化任务运维指南
description: 配置保留期和存储、检查任务状态、诊断重放分歧、管理工作流版本、中止停滞任务、处理死信，以及规划持久化任务的负载容量。
ms.date: 10/16/2026
ms.topic: how-to
---

<a id="operate-durable-tasks"></a>

# 持久化任务运维指南

本指南介绍如何配置和运维[持久化任务](durable-tasks.md)中所述的持久化任务运行时。持久化任务基于 Messaging 运行，因此[消息传递运维指南](messaging-operations.md)也适用于承载调用、结果、取消请求、信号及延迟恢复操作的消息。

<a id="configure-storage-and-retention"></a>

## 配置存储和保留期

在 Silo 构建器上调用 `AddDurableTasks` 会添加 Messaging，后者又会添加 Durable Jobs，并将任务状态作为名为 `orleans.durable-tasks.runtime` 的日志化状态存储在各 Grain 的日志中。应用程序状态应使用其他名称。任务状态采用 Grain 的日志格式，即 JSON 或 Orleans 二进制格式；`AddDurableTasks` 注册任务状态的 JSON 元数据，而请求、结果和信号值则作为经 Orleans 序列化的有效载荷存储。Grain 的日志是任务状态的事实来源；测试中可使用 `AddVolatileJournalStorage` 等易失性日志存储，将日志保存在内存中。未配置日志化状态管理器的持久化 Grain 将无法完成激活初始化。

`DurableTaskOptions` 通过 `AddDurableTasks` 的回调配置保留期和停滞执行相关设置，并在启动时接受验证。

| 设置 | 默认值 | 运维影响 |
| --- | --- | --- |
| `ResultRetentionPeriod` | 1 天 | 对于没有调用方 Grain 的任务（如由客户端调度的任务或分离任务），在此期间保留其终态结果供轮询；完成消息转入死信后也会保留该结果这么长时间。任务完成后的这一期间内，使用任务 ID 对调度请求去重。不得为负值。 |
| `StalledExecutionRetryInterval` | 1 分钟 | 持有[停滞执行](durable-tasks-versioning.md#stalled-executions)的 Grain 被唤醒的间隔，使当前激活实例结束后，后续激活实例可以使用自身运行的代码重放该执行。必须为正值。 |

`DurableMessagingOptions` 中的 Messaging 设置控制传输过程；参见[容量、重试和保留期](messaging-operations.md#capacity-retries-and-retention)。

- `OutboxRetryTimeout` 限制调用、完成、取消或信号消息投递失败后，发件箱将其转入死信之前持续重试的时长。
- `DeduplicationWindow` 与处理其他消息一样，对重复投递的持久化任务消息去重。它同时是调用接纳窗口：从发送方首次暂存调用开始，在此期间内 Grain 会接纳该调用；如果背压或投递管道使调用滞留至超出此期限，则将该调用转入死信，代码为 `durable-rpc/expired`。早于对应调用到达的取消请求或信号会保留两倍于该期限的时间，以涵盖发送方与 Grain 之间的时钟偏差，从而使获接纳的调用以已取消状态终止或观察到该信号。如果对应调用未在此期间到达，则删除该信号，并由 Grain 记录警告。
- 调用、完成、取消和信号消息直接被接纳到任务状态中，因此不占用收件箱容量。延迟恢复消息是 Grain 向自身发送的调度消息，与其他调度消息一样，不计入 `MaxCapacity`；对于每个已调度的延迟，Grain 会保留一条恢复消息，直至该延迟到期。

多 Silo 部署应配置共享的生产级 Journaling 和 Durable Jobs 提供程序；参见[Journaling 配置](journaling/configuration.md)。

<a id="retention-and-deletion"></a>

## 保留与删除

每个被调用的任务都拥有一棵执行树：包括任务本身、其本地步骤、延迟、组合器记录、`DurableTask.RunInline` 记录、版本决策、信号记录、对其他任务的订阅、分离任务记录、信号发送标记，以及该任务对其他 Grain 的调用记录。Grain 通过一次日志写入删除整棵树：

| 调用方 | 删除时机 |
| --- | --- |
| 另一持久化 Grain 的持久化任务或普通方法 | 调用方 Grain 持久化接纳结果后，发件箱退役该完成消息时。 |
| 客户端、普通 Grain 方法，或将其分离的持久化任务 | 完成后经过 `ResultRetentionPeriod`。 |
| 任意调用方，且完成消息已转入死信 | 转入死信后经过 `ResultRetentionPeriod`。 |

持久化任务通过 `GetDurableTask` 或通过将某个任务作为分离任务启动来订阅该任务时，被订阅的任务还会保留至发件箱退役发往每个订阅者的完成消息为止。订阅者的完成消息转入死信时，与调用方的完成消息一样，会重新开始计算保留期。

由普通方法调度的调用在 Grain 中具有独立的根记录，该记录在结果到达后保留 `ResultRetentionPeriod`。

删除执行树会取消其未完成的任务：删除操作的日志写入向每个尚未完成的跨 Grain 调用发送取消请求；在该次写入之后，向正在运行的执行发出取消信号，并丢弃它们的结果。对于已删除任务，后来到达的完成消息及延迟恢复消息均被忽略。删除后，轮询会抛出 `DurableTaskNotFoundException`，而使用相同任务 ID 重新调度会再次执行该请求。

Grain 在激活时、每次日志写入后，以及响应轮询或客户端调度请求之前应用保留策略，因此轮询和调度均不会观察到保留期已结束的任务。空闲 Grain 会将过期状态保留在日志中，直至上述某个时机发生。

设置 `ResultRetentionPeriod` 时，应涵盖客户端轮询结果或重试调度的最长时间间隔，并考虑每个 Grain 日志中保留的任务状态所占空间：所保留的客户端调度任务数量等于任务完成速率乘以保留期。

<a id="observe-a-task"></a>

## 观察任务

Grain 引用提供任务控制能力：扩展方法 `GetDurableTask<TResult>(rootId)` 返回 `DurableTaskReference<TResult>`，可用于轮询、等待、取消或中止已有任务，持久化任务也可以通过等待该引用来订阅其结果；扩展方法 `SignalDurableTaskAsync` 和 `TrySignalDurableTaskAsync` 用于投递信号。`GetStatusAsync` 报告任务的 `DurableTaskStatus`，而 `GetResponseAsync` 返回包含结果或异常的响应。轮询 Grain 未保留的任务时会抛出 `DurableTaskNotFoundException`：

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_diagnostics" language="csharp":::

| 状态 | 含义 |
| --- | --- |
| `Pending` | 任务尚未记录结果，包括取消请求或延迟尚未完成的情形。 |
| `CompletedSuccessfully` | 任务已成功完成。 |
| `Canceled` | 任务已取消。 |
| `Failed` | 任务失败或被中止；中止对应的异常为 `DurableTaskAbortedException`，其消息为中止原因。 |

Grain 通过从其激活服务中解析得到的 `IDurableTaskManager`，无需发起 Grain 调用即可观察、取消、中止自身的任务，并向其发送信号。

<a id="observe-runtime-transitions"></a>

## 观察运行时状态转换

`Orleans.DurableTasks` 诊断侦听器 `DurableTaskEvents` 会在执行相应转换的激活实例上，由运行时暂存该转换时同步报告每次转换，同时提供激活实例的 `IGrainContext` 和任务 ID。该变更随 Grain 的下一次日志写入提交。

| 事件 | 触发时机 |
| --- | --- |
| `TaskCreated` | 运行时暂存任务状态时：该任务可以是被调用的任务，也可以是某次执行中的操作。 |
| `TaskCompleted` | 运行时暂存任务的终态结果时；该事件携带此结果。 |
| `TaskDeleted` | 运行时暂存任务删除操作时，包括随执行树删除或保留期结束时删除。 |
| `ExecutionStarted` | 在激活实例上启动任务体的一次执行尝试时。 |
| `ExecutionStopped` | 执行尝试结束时：任务体完成，或中止或删除操作取消该次尝试。 |
| `ReplayEnded` | 重放中的执行尝试启动先前任何尝试均未记录的操作时，此后 `IsReplaying` 报告 `false`。 |
| `SignalWaitStarted`, `SignalWaitEnded` | 某次执行开始或结束等待信号时，事件归属于该信号的记录。 |
| `SelectionWaitStarted` | 某次选择（如 `DurableTask.WhenAny` 的选择）未找到已完成的候选项而开始等待时，事件归属于该次选择决策。 |
| `VersionRecorded` | 某次执行记录其所遵循的变更版本时，事件携带变更名称及版本。 |
| `ExecutionStalled` | 当前运行代码无法重放某次执行而导致该次尝试停滞时；任务仍为待完成状态。`Reason` 为 `UnsupportedVersion` 或 `Diverged`，`Exception` 描述停滞原因。参见[停滞的执行](durable-tasks-versioning.md#stalled-executions)。 |

测试可订阅 `DurableTaskEvents.AllEvents`，或使用 `DiagnosticEventCollector` 等待状态转换，而无需轮询。Messaging 通过 `MessagingEvents` 报告投递、重试和退役，其 `InboxMessageReceiving` 观察者可注入投递故障；参见[观察进度与规划系统容量](messaging-operations.md#observe-progress-and-size-the-system)。

<a id="diagnose-replay-divergence"></a>

## 诊断重放分歧

重放必须在每个已记录的 ID 下调度相同的操作，并且必须能够解码每个已记录的结果。操作的种类和目标相同，就视为同一操作：对于调用而言，无论参数是什么，只要在同一 Grain 上调用同一接口方法，就是同一操作。因此，参数发生变化或序列化形式不同的调用也会返回已记录的结果。当重放调度了不同的操作，或无法再解码已记录的结果时，执行会停滞：运行时以 `DurableTaskAttemptCancellationReason.Diverged` 为原因取消本次执行尝试，丢弃其结果，并使任务保持待完成状态。运行时记录警告并触发 `ExecutionStalled`，其 `Exception` 描述重放分歧，例如：

```text
Durable task 'place-order-…/authorize-payment' is already associated with a different request.
It records a call to method '…Invokable_IPaymentGrain_…_Authorize…' on grain 'payment/…'
(fingerprint 3F2A9C01B7E4), and this attempt requested a call to method
'…Invokable_IPaymentGrain_…_Capture…' on grain 'payment/…' (fingerprint 91C0D4E2A6B8). A replay
schedules the same operation under each identifier: a call or request invokes the same method on
the same target, whatever its arguments; new workflow steps use new identifiers.
```

该消息指出已记录的操作以及本次请求的操作，例如 Grain 调用、延迟、`WhenAny` 决策、`DurableTask.RunInline` 效果操作、分离任务启动或信号。调用通过其生成的可调用类型标识方法，其指纹是序列化参数的哈希值，用于在诊断信息中区分调用，但不参与判定调用是否匹配。典型原因如下：

- 一次部署将某个步骤中的调用替换为对其他方法或 Grain 的调用，调整了未命名子操作的顺序，或在现有操作之前添加了未命名的子操作或组合器。应使用 `DurableTask.GetVersion` 对此类变更进行分支，使待完成的执行保留其已记录的操作。
- 循环或条件分支调度子操作时，其 ID 依赖非确定性值。
- 一次部署修改了已取消任务的任务体，导致重放在先前执行观察到取消的取消点之前结束。消息会指出重放到达的位置和已记录的位置。

在子操作之间的代码中读取重放时可能不同的值（如 `DateTime.UtcNow`、随机数或可变 Grain 状态），并将其传给子操作，并不会造成重放分歧：重放会读取首次运行使用首次取得的值发起调用时记录的结果。`DurableTask.RunInline` 会记录这类值，使每次运行都将相同的值传给后续代码。

任务体以本次尝试的 `DurableTaskAttemptCanceledException` 观察到重放分歧，该异常的内部异常为分歧异常，且本次尝试后续的持久化操作也会抛出它。等待该任务的调用方会继续等待。Grain 每隔 `StalledExecutionRetryInterval` 保留一次唤醒安排，以便当前激活实例结束后，后续激活实例重放任务：部署与记录匹配的代码（例如使用 `DurableTask.GetVersion` 对变更进行分支的代码），执行便可恢复。若要结束任务而非恢复，则应中止它；参见[中止卡住的任务](#abort-stuck-tasks)。监视 `ExecutionStalled` 可发现停滞的执行。

<a id="version-workflows-safely"></a>

### 安全地管理工作流版本

当部署变更工作流而仍有待完成执行时，这些执行将使用新代码重放：

- 保留已有步骤、`RunInline` 效果操作和分离任务启动操作，以及其 ID、方法和目标。对于部署中变更了参数的调用或分离任务启动，在已记录该操作的执行中会返回原始参数对应的已记录结果。修改 `RunInline` 的委托不会影响已记录此操作的执行：它们返回已记录的结果。同名的效果操作与步骤共享 ID，因此将已记录步骤改为效果操作或反向修改会导致重放分歧；应使用 `DurableTask.GetVersion` 对此变更进行分支。
- 为新步骤分配新 ID，并将依赖新行为的步骤放在已有步骤之后。
- 为今后可能被调整位置的每个子操作和组合器命名，以免生成的 ID 发生偏移。分离任务和信号发送操作根据分离任务或信号本身派生记录 ID，因此不会偏移。未命名的分离任务根据执行中的未命名操作派生其根 ID，因此若未来变更可能调整其位置，应使用 `WithId` 为其命名。
- 保持参数、结果和信号类型可由 Orleans 序列化器解码，添加成员时使用新的 `[Id(n)]` 值。若重放无法解码已记录的结果，就会发生重放分歧。
- 修改延迟时长只影响新延迟；重放的延迟保留其已记录的到期时间，运行时会记录警告。

若要变更待完成执行已经经过的操作，例如替换步骤或修改参数，请使用 `DurableTask.GetVersion` 进行分支：每次执行遵循其已记录的版本，而在较早代码上启动的执行遵循版本 `0`。参见[持久化任务版本管理](durable-tasks-versioning.md)。

若要退役旧工作流结构，应将新结构添加为新方法，使用该方法调度新工作，并保留旧方法直至其待完成任务全部结束。

<a id="abort-stuck-tasks"></a>

## 中止卡住的任务

对于任务体忽略取消请求或补偿操作无法完成的任务，可以将其中止：

- 在拥有任务的 Grain 内，使用 `IDurableTaskManager.AbortAsync(rootId, reason)`。
- 从客户端或工具中，使用 `grain.GetDurableTask<TResult>(rootId).AbortAsync(reason)`。

中止操作通过一次日志写入，将任务及其执行树以 `DurableTaskAbortedException` 提交为失败状态，并向每个尚未完成的跨 Grain 调用发送中止请求，由被调用方中止自身的执行树，同时取消正在运行的执行尝试并丢弃其结果。已取消尝试中的持久化操作会抛出 `DurableTaskAttemptCanceledException`，原因为 `TaskAborted`，且 `IsTaskCancellationRequested` 保持为 `false`，因此中止不会执行补偿；任何已产生的部分效果都须另行撤销。执行尝试退出过程中的代码会在 `finally` 块中释放进程内资源，其日志化状态的变更随 Grain 下一次日志写入提交。中止已处于终态的任务不会产生任何变更，分离任务需单独中止。此后，诊断信息将任务报告为 `Faulted`，异常为 `DurableTaskAbortedException: <reason>`。

<a id="dead-letters"></a>

## 死信

持久化任务消息使用以 `durable-rpc/` 开头的主题：`invoke`、`complete`、`cancel`、`resume` 和 `signal`。Messaging 的重试和死信策略适用于这些消息；可通过 `IMessagingDiagnostics` 检查和移除死信，详见[检查与重放死信](messaging-operations.md#inspect-and-replay-dead-letters)。

- 接收方 Grain 永久拒绝某条消息时，其收件箱将该消息记录为已处理，并记录拒绝原因和代码。此类情况包括任务 ID 已标识其他请求或调用方的调用，以及被拒绝的信号（如信号值冲突或目标任务已完成）。发生冲突的调用携带来自 `DurableTaskRejectionCodes` 的 `durable-rpc/conflict` 代码，发送方的发件箱在将该调用作为已拒绝调用退役时报告此代码。
- 接收方 Grain 因其他原因无法接纳的消息（如暂时性故障或无法解码的有效载荷）会被重试。在滚动部署期间，重试可能到达能够解码该消息的激活实例。
- 投递持续失败达到 `OutboxRetryTimeout` 时，发件箱将消息转入死信。
- 如果完成消息中存储的结果无法再解码（例如部署移除了结果类型），发件箱会将该完成消息转入死信，并以解码错误作为原因。运行时退役该完成消息，并将任务保留 `ResultRetentionPeriod`；读取结果的操作（如轮询）会报告解码错误。Grain 继续处理其他工作。
- 若任务中存储的请求无法再解码（例如部署移除了参数所包含的类型），该任务会停滞，但不会导致 Grain 激活失败：Grain 继续运行其他任务，轮询将该任务报告为 `Pending`，`ExecutionStalled` 以 `Diverged` 为原因并携带解码错误报告它。已存储的请求会保留，因此后续能够解码请求的激活实例可运行该任务；若要结束该任务，则应中止它。
- 如果任务的结果或异常无法序列化（例如某个值的类型没有编解码器，或异常数据包含此类值），任务将以 `SerializerException` 失败，该异常指出相应类型。该失败与其他结果一样提交，Grain 继续提交其他工作。

完成消息转入死信后，调用方的执行仍处于等待状态。应决定是取消还是中止该执行，并仅在纠正死信产生的原因后才重放该死信。

当启动操作被目标拒绝或未能投递时，发起任务中的相应记录将标记为失败，因此 `ScheduleAsync` 以及等待任务或其句柄的操作会在每次重放时抛出该失败：

| 结果 | 拒绝代码 | 失败异常 |
| --- | --- | --- |
| 任务 ID 已标识另一请求或调用方。目标立即拒绝该调用。 | `durable-rpc/conflict` | `DurableTaskConflictException` |
| 调用在目标的接纳窗口之后到达，该窗口为自发起方首次暂存调用起的 `DeduplicationWindow`。 | `durable-rpc/expired` | `DurableTaskRejectedException` |
| 目标以其他代码拒绝调用，或拒绝时未提供代码。 | 目标的代码 | `DurableTaskRejectedException` |
| 由于暂时性故障或无法解码的请求，目标未在 `OutboxRetryTimeout` 内接纳调用，或发件箱无法到达目标。 | | `DurableTaskDeliveryException`，携带死信原因 |

调用方（如 HTTP 端点）可根据异常类型区分冲突与未被接纳的启动操作；冲突可以通过选择其他任务 ID 解决。`DurableTaskRejectedException.Code` 报告目标的拒绝代码，`Target` 报告拒绝调用的 Grain。

<a id="client-waits-across-outages"></a>

### 客户端在服务中断期间的等待行为

客户端或普通 Grain 方法通过轮询任务所属的 Grain 来等待任务。当轮询因连接断开、Grain 所在 Silo 停止或崩溃、调用超时，或 Grain 停用（此时轮询以 `DurableTaskAttemptCanceledException` 失败，原因为 `ActivationStopping`）而无法访问 Grain 时，任务仍会继续运行。因此等待操作会采用从 50 毫秒到 5 秒的指数退避进行重试，后续轮询将到达 Grain 的后续激活实例。等待操作将在获得任务结果、因 Grain 未保留任务而抛出 `DurableTaskNotFoundException`，或调用方的取消令牌触发或等待操作超时时结束。

<a id="capacity-and-throughput"></a>

## 容量与吞吐量

- 已记录的效果操作 `DurableTask.RunInline` 不会额外增加一次日志写入：其记录及其变更的状态随任务下一次持久化写入提交。每个效果操作保留一条包含其结果的记录，直至任务的执行树被删除。
- 本地步骤需要一次日志写入：其记录随该步骤首次持久化写入提交，因此变更状态后不经等待直接返回的步骤，会将状态变更和结果一同提交。
- 组合器和 `ScheduleAll` 并发调度其输入，运行时将这些调用合并到一次日志写入中。
- 每个请求和结果有效载荷在每个 Grain 中仅记入日志一次，更新元数据不会重写有效载荷。完成消息转入死信后，其结果也由死信保留，同时任务状态会将其保留 `ResultRetentionPeriod`。完成序号为每个任务的状态增加一个整数。
- 每个执行片段（即两次持久化等待之间的代码）作为激活实例上的请求运行，遵守 Grain 的可重入性规则。在不可重入的 Grain 上，等待其他工作（例如调用另一 Grain）的执行片段会像 Grain 方法一样占用该 Grain，同一 Grain 的并发步骤每次只能运行一个片段。热点 Grain 会限制其工作流的吞吐量，因此应按业务所有者对工作流进行分区，例如每个订单对应一个 Grain。
- 轮询与 Grain 的其他工作交错执行，因此等待中的客户端不会延迟用于完成任务的工作。
- 删除执行树的耗时与其规模成正比；保留的执行树会增大 Grain 的日志快照，直至其被删除。

监控 Messaging 中的收件箱和发件箱深度、重试及死信指标，这些指标包含持久化任务消息；参见[观察进度与规划系统容量](messaging-operations.md#observe-progress-and-size-the-system)。

<a id="known-constraints"></a>

## 已知限制

- 持久性覆盖参与 Journaling 和 Messaging 提交的状态与消息。外部副作用需要自身的幂等性机制，例如根据任务 ID 派生请求键。
- 持久化任务直接将自身 Grain 的持久化方法作为本地步骤调用。通过自身 Grain 的引用进行持久化调用会被拒绝。
- 持久化任务使用 `SignalDurableTaskAsync` 向其他 Grain 发送信号，该方法记录发送操作以供重放；`TrySignalDurableTaskAsync` 用于客户端和普通 Grain 方法。
- 信号以被调用的任务为目标：即由客户端、其他 Grain 或将任务分离的持久化任务启动的任务。
- `ScheduleAll`、`WhenAllCompleted` 和 `WhenEach` 只有在持久化执行内部被等待时才运行；将其调度为独立任务会抛出 `NotSupportedException`。
- 激活实例关闭时，停止启动新的执行，丢弃处于挂起等待状态的执行尝试，允许正在运行的片段执行完毕但丢弃其结果，并将已提交工作留待下次激活时重放。
- 适配器及抽象目前尚未首次发布到 NuGet，在集成评估期间，其持久化格式可能发生变化。
