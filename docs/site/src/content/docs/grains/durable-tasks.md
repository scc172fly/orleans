---
title: 持久化任务
description: 了解持久化任务编程模型及其重放、提交保证、取消机制、信号和任务生命周期。
ms.date: 10/14/2026
ms.topic: conceptual
---

<a id="durable-tasks"></a>

# 持久化任务

Orleans 持久化任务将返回 <xref:Orleans.Durable.Tasks.DurableTask> 或 <xref:Orleans.Durable.Tasks.DurableTask`1> 的 Grain 方法作为持久化执行实例运行。工作流由普通的顺序 C# 代码编写：它等待对其他 Grain 的调用、本地状态转换、延迟和外部信号。每个步骤的结果都会记录到 Grain 的日志中，因此即使激活实例丢失，工作流也能从中断处恢复执行。持久化任务基于[消息传递（Messaging）](messaging.md)和 Durable Jobs 运行：前者通过各 Grain 的持久化收件箱和发件箱在 Grain 之间传递调用、结果、取消请求和信号；后者在延迟到期时唤醒 Grain，并在 Grain 停用后恢复其执行实例。

> [!IMPORTANT]
> `Microsoft.Orleans.Durable` 和 `Microsoft.Orleans.Durable.Abstractions` 是面向 .NET 11（`net11.0`）的预发行 Alpha 包，尚未首次发布到 NuGet。由于其 API 和持久化格式在稳定之前可能变化，请在制定明确的升级、回滚、备份及恢复计划后进行评估。Orleans 仓库中的项目以源码项目的形式引用它们。

本页介绍编程模型及其保证。可继续阅读：

- [任务标识与引用](durable-tasks-identity.md)：任务 ID 的构造方式、保持 ID 稳定的变更，以及如何从其他位置寻址任务。
- [结构化并发](durable-tasks-structured-concurrency.md)：任务树、所有权、生命周期及取消请求如何在其中传播。
- [幂等性](durable-tasks-idempotency.md)：恰好提交一次的效果，以及需要幂等键的外部效果。
- [重放安全规则](durable-tasks-replay-rules.md)：持久化任务分析器强制执行的规则。
- [版本管理](durable-tasks-versioning.md)：在执行实例尚未完成时修改工作流代码，以及停用旧版本。
- [与其他系统集成](durable-tasks-integrations.md)：支付提供程序、数据库、HTTP 服务、Webhook 及重试的位置。
- [实践指南](durable-tasks-recipes.md)：扇出、竞速、补偿、信号、分离任务、取消以及从其他 Grain 调度工作流。
- [运维与故障排查](durable-tasks-operations.md)：配置、保留期、诊断、重放分歧、工作流版本管理和容量。

<a id="choose-durable-tasks-or-message-handlers"></a>

## 选择持久化任务还是消息处理器

两种模型都在 Grain 的日志中提交业务状态、收到的消息和发出的消息，并且每个 Grain 共用一个收件箱和一个发件箱。

| 适合使用持久化任务的场景 | 适合使用[消息处理器](messaging.md)的场景 |
| --- | --- |
| 以顺序代码编写多步骤工作流，步骤之间传递结果。 | 响应单个命令和事件，每条消息调用一次处理器。 |
| 对持久化调用执行扇出、竞速和按完成顺序处理。 | 由应用程序定义消息标识、主题、标头及原始有效载荷。 |
| 工作流中的持久化定时器、截止时间以及对外部事件的等待。 | 由应用程序显式建模的定时消息和状态机。 |
| 协作式取消，支持内联补偿并记录取消原因。 | 以强类型消息交换业务回复和拒绝结果。 |

<a id="define-durable-task-methods"></a>

## 定义持久化任务方法

Grain 接口通过返回 `DurableTask` 或 `DurableTask<TResult>` 来声明持久化方法。参数和结果会跨 Grain 传递，并持久化在任务状态中，因此其类型需要 Orleans 序列化器：

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_contracts" language="csharp":::

实现该接口的 Grain 继承自 <xref:Orleans.Durable.Journaling.DurableGrain>。Grain 的持久化状态变更与任务结果在同一次日志写入中提交，调用方在写入完成后收到结果：

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_catalog_grain" language="csharp":::

`async DurableTask` 方法体是延迟执行的定义：调用方法时创建该定义，持久化任务运行时在该任务的标识下执行它。无需等待即可完成的方法体也是一个普通步骤。

在持久化任务内部，等待持久化调用会将其作为当前执行实例的子任务运行。从客户端或普通 Grain 方法发起调用时，等待该调用或调用 <xref:Orleans.Durable.Tasks.DurableTaskExtensions.ScheduleAsync*> 会将其作为根任务启动。[启动与观察任务](#starting-and-observing-tasks)介绍启动任务的所有方式。

<a id="example-an-order-workflow"></a>

## 示例：订单工作流

下面的 Grain 将一笔订单作为一个持久化任务运行。它将每次状态变更作为已记录效果执行，等待宽限期结束，并发预留每项订单明细的库存，授权支付，然后等待发货信号。普通 Grain 方法可以携带原因取消工作流、中止工作流以及传递发货信号。如果工作流被取消、某项商品缺货或支付被拒绝，工作流会内联释放已预留的库存：

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_order_grain" language="csharp":::

购物篮 Grain 以根任务 ID `place-order-{orderId}` 启动工作流。停用后，下一个激活实例从头重放 `PlaceOrderAsync`：`submit`、`grace-period`、`validate` 和 `reserve` 的输入操作返回各自已记录的结果，工作流从第一个尚未完成的操作继续。Orleans 仓库中的 `playground/DurableOrderProcessing` 项目运行该工作流的完整版本，提供浏览器界面，通过持久化 HTTP 操作下单，并使用分离式投影更新及诊断端点。

<a id="task-identity"></a>

## 任务标识

持久化任务由其 Grain 和 <xref:Orleans.Durable.Tasks.TaskId> 共同标识。任务 ID 具有层次结构：启动调用方选择根 ID，之后每层嵌套附加一个片段。[任务标识与引用](durable-tasks-identity.md)列出了各种 ID 及其稳定性。上面的订单工作流生成以下任务树：

```text
place-order-{orderId}
place-order-{orderId}/submit
place-order-{orderId}/grace-period
place-order-{orderId}/validate
place-order-{orderId}/reserve/0
place-order-{orderId}/reserve/1
place-order-{orderId}/authorize-payment
place-order-{orderId}/pay
place-order-{orderId}/$signal:shipped
place-order-{orderId}/ship
```

- <xref:Orleans.Durable.Tasks.DurableTaskExtensions.WithId*> 使用稳定的片段为子任务命名。如果一次运行中重复使用同一名称（例如在循环中），后续出现的任务按出现顺序标识：`line`、`line$2`、`line$3`。等待未命名的子任务时，会获得自动生成的片段，以 `$` 开头，遵循执行实例创建未命名子任务的顺序。名称不得包含 `$`。
- 命名的组合器会在自身 ID 下为输入分配 ID：`DurableTask.WhenAll(...).WithId("reserve")` 按输入顺序将输入标识为 `reserve/0`、`reserve/1`，依此类推。
- 对另一个 Grain 的调用在目标 Grain 上的调用方作用域内运行：`$caller/{callerGrainId}/{incarnation}/{taskId}`，其中 `{incarnation}` 标识调用方的运行世代。订单中的调用记录 `place-order-{orderId}/reserve/0` 对应商品目录 Grain 上的任务 `$caller/order/{orderId}/{incarnation}/place-order-{orderId}/reserve/0`。每个调用方拥有自己的作用域，因此即使多个调用方的工作流根 ID 相同，也不会共享被调用方的任务。同一根 ID 的每次运行都有独立的运行世代，因此在先前运行被删除后再次运行会调用新任务。根任务（例如分离启动的任务）在其所在 Grain 上保留自己的根 ID，而根 ID `$caller` 是保留值。
- 从同一调用方再次以相同方法调度同一任务 ID，会重新关联到现有任务及其已记录结果，而不论参数为何。因此，即使重试时参数的序列化结果不同，也会找到同一任务。以不同接口或方法调度同一 ID 会失败，因此只要 Grain 仍保留任务，该任务 ID 就能对调度请求去重。

持久化任务直接调用自身 Grain 的持久化方法，将其作为本地步骤，例如 `await RefreshPricesAsync().WithId("refresh")`。通过自身 Grain 的引用发起持久化调用会被拒绝。

<a id="recorded-effects-local-steps-and-calls-to-other-grains"></a>

## 已记录效果、本地步骤与其他 Grain 调用

*已记录效果（recorded effect）* <xref:Orleans.Durable.Tasks.DurableTask.RunInline*?displayProperty=nameWithType> 在返回前于调用方当前工作项中执行同步代码，不挂起，也不创建子执行实例，并以其名称将结果或抛出的异常记录到任务树中。该记录与效果修改的日志化状态在同一次日志写入中提交；重放时直接返回已记录结果，不再执行该效果。订单工作流以此方式修改状态：

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_run_inline" language="csharp":::

- 效果恰好一次地修改 Grain 状态，读取之后可能被其他操作修改的状态，并生成标识符、随机值及实际时钟时间，使每次重放都观察到相同的值。
- 效果通过返回结果向工作流传递数据：后续运行不会再次对捕获变量进行赋值。
- 失败也会被记录，因为日志化状态没有回滚机制：抛出异常前的修改会与已记录异常一同提交。无法序列化的结果也会记录为该失败。如果运行在日志写入提交该记录前结束，效果所做的状态修改也会丢失，因此下一次运行会再次执行效果。
- 同一同步代码块中的连续效果及其修改的日志化状态在同一次日志写入中提交，Grain 不会在这些效果之间执行其他轮次。从效果返回并不表示其记录已提交：它在任务的下一次日志写入时提交。任务取消后仍会运行效果，因此补偿操作也能记录自身的效果。
- 名称标识效果的方式与 `WithId` 标识步骤相同：第一次为 `pay`，同一次运行中后续出现时为 `pay$2`。
- `DurableTask.RunInline(effect)` 可省略名称，例如 `var reference = DurableTask.RunInline(Guid.NewGuid);`。效果使用执行实例的下一个自动生成的子 ID，例如 `$child-3`；未命名效果与未命名子任务共享该序列。因此，只要每次运行按相同顺序创建未命名操作，就能找到对应记录。如果效果的位置可能随运行或部署变化，应为其命名。
- 效果是同步的：异步效果和持久化效果无法编译，错误会给出替代方式 `await DurableTask.Run(async () => ...)`。效果在激活实例的调度器上运行，因此在 `ConfigureAwait(false)` 之后调用 `RunInline` 会抛出 `InvalidOperationException`，且不执行效果。
- 记录仅适用于持久化任务内部。在持久化任务之外（例如普通 Grain 方法共用的辅助方法中），效果每次都会执行。

*本地步骤*是同一 Grain 中的 `async DurableTask` 方法，作为子任务被等待，用于需要异步等待的工作。它将结果记录到 Grain 的任务状态中，该记录在首次持久化写入时提交（写入内容可以是结果，也可以是该步骤调度的子任务、延迟或消息）。其结果暂存于运行最终代码的工作项中，因此最终代码所做的状态修改与结果一起提交。重放时，已记录结果的步骤直接返回该结果，不再运行。

*对其他 Grain 的调用*属于持久化调用。调用方 Grain 将调用记录到任务状态，并将调用暂存到发件箱；两者随执行实例的下一次日志写入一同提交。被调用方的收件箱直接将调用接纳到任务状态中，运行相应方法，并通过调用方的收件箱返回结果，由调用方将结果记录到自身任务状态。消息使用根据发送方、接收方、任务和主题派生的标识，从而对重复投递和重放时重复发送的消息去重。

<xref:Orleans.Durable.Tasks.DurableTask.Run*?displayProperty=nameWithType> 为非 Grain 调用的工作（例如调用外部服务）创建尚未启动的步骤。当该步骤被等待、调度或投递时，才执行其委托，可能在后续轮次中运行；它是具有独立 `DurableTask.Current` 和取消作用域的子执行实例。创建步骤本身不会启动任何工作，也不会在线程池上执行。重放时返回委托的已记录结果；如果结果未被记录，则重新运行委托，因此委托的外部效果需要自行保证幂等性。

<a id="effects-and-steps"></a>

### 效果与步骤

`DurableTask.RunInline("hold-id", Guid.NewGuid)` 和 `await DurableTask.Run(async () => ...).WithId("hold-id")` 使用相同的标识 `{taskId}/hold-id`，并且都只记录一次结果。两者的区别在于代码运行的时机，其他差异由此产生。“内联”的含义与 .NET 中其他位置（例如 `TaskScheduler.TryExecuteTaskInline`）一致：代码在当前工作项中同步运行，而不是经调度后运行。

- 效果在 `RunInline` 返回前立即于当前工作项中运行。因此，连续效果与其所修改的状态一同提交；效果观察到的是调用方的 `DurableTask.Current`，并且在任务取消后仍会执行。
- 步骤在被等待、调度或投递时作为子执行实例运行，可能在后续轮次中运行。因此，它具有自己的 `DurableTask.Current`；若父任务在未受屏蔽的步骤启动前已请求取消，该步骤会以取消状态完成；步骤的记录通过独立的日志写入提交。

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_effect_and_step" language="csharp":::

重放时，如果已记录的操作类型与当前方法体运行的另一种操作类型不同，就会发生重放分歧，而非重新执行工作：执行实例陷入停滞，`ExecutionStalled` 会指出这两种类型。将效果变更为步骤或将步骤变更为效果的部署，应使用 [`DurableTask.GetVersion`](durable-tasks-versioning.md) 进行版本分支，使记录了旧类型的执行实例继续使用旧类型。

<a id="publish-progress-and-stream-drafts"></a>

## 发布进度和流式传输草稿

工作流通过 Grain 的日志化 <xref:Orleans.Durable.Journaling.IDurableLog`1> 向观察者发布进度。观察者通过 <xref:Orleans.Durable.Journaling.IDurableLog`1.ReadAsync*> 读取日志，通常通过 `IAsyncEnumerable<T>` Grain 方法访问。[观察日志化状态](journaling/observe-state.md)介绍了如何读取、恢复读取及裁剪日志。

要发布日志条目，可在已记录效果 `DurableTask.RunInline(() => log.Append(item))` 中追加条目。效果在任务中的该位置仅追加一次，条目与效果记录在任务的下一次持久化写入中一起提交。重放返回已记录的序列号而不再追加，因此以已提交模式读取的读取方按任务追加顺序接收每条条目一次。使用任务参数、已记录结果和逻辑时间计算各条目，以便每次重放都在相同位置执行相同的追加操作。条目适合发布调用方和用户界面关注的进度，例如订单的各个状态或导出操作中已完成的各个分区：

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_progress" language="csharp":::

`playground/DurableOrderProcessing` 项目将各订单的状态保存为快照日志，由工作流以这种方式追加快照，并通过服务器发送事件（SSE）流式传输到浏览器。

若要在生成内容的同时流式传输（例如模型生成的响应），应在本地步骤中运行该流。步骤使用 <xref:Orleans.Durable.Journaling.IDurableLog`1.BeginDraft*> 打开草稿，将每个项目写入草稿，并在最后一次 `await` 之后的最终同步代码中提交完整内容：

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_stream_draft" language="csharp":::

- 草稿中的项目是易失的。向 `ReadAsync` 传入 `includeDrafts: true` 的读取方会在步骤写入各项目时收到它们；当 `Commit` 追加替代草稿的日志条目时，内容成为持久化内容。
- 步骤的结果暂存于运行最终同步代码的工作项中，因此 `Commit` 追加的条目与步骤的已记录结果在同一次日志写入中提交。以已提交模式读取的读取方在该写入持久化后收到条目。
- 因此，内容恰好提交一次：任何已提交日志要么同时包含这些条目与步骤记录，要么两者均不包含。在该写入之后重放会返回已记录结果而不运行步骤。如果运行在该写入前结束，两者都会丢失，下一次运行会再次执行步骤、流式传输并提交内容。
- 如果步骤抛出异常（包括取消之后），会释放并放弃草稿，并记录步骤失败，因此重放会重新抛出异常而不再次流式传输。
- 步骤中的日志化变更应放在最终同步代码中。如果步骤在某个 `await` 之前做出变更（例如 `Append`），这些变更可能随着其他 Grain 工作在步骤完成前请求的日志写入而提交，重放重新运行步骤时就会再次做出该变更。

<a id="replay-and-determinism"></a>

## 重放与确定性

停用或发生故障后，下一个激活实例重放每个待完成的任务：方法体从头运行，每个已记录操作返回其已记录结果。重放在 Grain 的 `OnActivateAsync` 完成后开始；停用时，在 Grain 的 `OnDeactivateAsync` 运行前会取消正在运行的任务的执行尝试，因此它们随后执行的持久化操作会抛出异常。请参阅[任务取消与执行尝试取消](durable-tasks-structured-concurrency.md#task-cancellation-and-attempt-cancellation)。要使工作流正确恢复，每次重放都必须按相同顺序调度具有相同 ID 和参数的相同操作。

重放不会重建 Grain 的日志化状态，因为它保存的是最新的已提交变更，其中包括后续操作所做的变更。因此，持久化方法体包含三种代码：

- **已记录操作**在每个执行实例中运行一次，重放直接返回其已记录结果，包括对其他 Grain 的持久化调用、本地步骤、`DurableTask.Run` 委托、延迟、信号等待与发送、分离式启动和投递式启动，以及 `DurableTask.RunInline` 效果。
- **重新执行的代码**位于方法体的最后一个已记录操作之前，每次重放都会重新运行。它根据方法参数、已记录结果、执行实例的逻辑时间和取消状态，以及执行实例尚未完成期间保持不变的配置计算值。
- **同步尾部代码**位于方法体的最后一个已记录操作之后，在方法体返回前不再有其他 `await`。它仅在方法体完成的某次运行中执行，并且其状态变更无论由哪个操作请求写入，都与任务结果在同一次日志写入中提交，因此重放不会再次运行它。

下面的 Grain 在效果中分配票号，授权支付，并在同步尾部代码中记录结果：

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_replay_regions" language="csharp":::

重新执行的代码应遵守以下规则：

- 将每个副作用放在已记录操作或同步尾部代码中。已记录操作之间的代码只根据方法参数、先前结果和每次重放均相同的其他值进行计算。
- 在已记录效果、步骤或同步尾部代码中读取和修改 Grain 状态。Grain 状态包含所有已提交变更，因此重新执行的代码可能读取到后续操作写入的状态。
- 从 `DurableTask.Current.UtcNow` 读取执行实例的逻辑时间，由 <xref:Orleans.Durable.Tasks.DurableTask.Current?displayProperty=nameWithType> 提供。运行时在执行实例的效果提交前持久化该时间，并在执行实例观察到各个子任务结果时将其推进到已记录的完成时间。因此，重放观察到相同的时间，延迟从其之前的工作完成时开始计时。
- 在已记录效果或子任务中生成随机值和标识符，并在步骤或 `DurableTask.Run` 委托中调用外部服务；这些外部效果需要自行保证幂等性。
- 当子任务按条件调度、在循环中调度或在组合器之外并发调度时，用 `WithId` 为其命名，也应为组合器命名。只有在未命名子任务按相同顺序创建时，自动生成的片段才保持稳定。分离任务和信号发送根据目标与根 ID 或信号派生 ID 进行记录，因此增加一项操作不会改变其他操作的 ID。
- 通过 `logger.ReplaySafe()` 返回的日志记录器记录日志，使每个事件只记录一次。

[重放安全规则](durable-tasks-replay-rules.md)列出了所有规则、规则所防止的故障及报告相应问题的分析器诊断项。

<xref:Orleans.Durable.Tasks.DurableTaskContext.IsReplaying?displayProperty=nameWithType> 报告本次运行是否正在重新执行之前运行已记录的操作：如果运行开始时发现任务树中有已记录操作（信号值除外），该属性从运行开始即为 `true`，直到运行到达所有这些操作，或开始执行先前从未记录过的操作，此后保持为 `false`。仅将其用于日志记录和诊断。如果一次运行在日志写入提交其工作之前结束，仍会被重放，因此仅在 `IsReplaying` 为 `false` 时运行的代码可能执行两次，也可能从未执行；根据该属性分支的代码则可能在不同运行中调度不同操作。

如果重放在已记录的 ID 下调度不同操作，或者无法解码已记录的结果，就会发生重放分歧：运行时使执行实例陷入停滞，该实例保持待完成状态，直到与记录匹配的代码重放它，同时引发 `ExecutionStalled`，指出已记录操作和请求的操作。请参阅[诊断重放分歧](durable-tasks-operations.md#diagnose-replay-divergence)。要在执行实例尚未完成时修改工作流，请使用 `DurableTask.GetVersion` 进行版本分支：它为各执行实例提供该变更所记录的版本，对于旧代码已运行经过变更位置的执行实例则返回版本 `0`。请参阅[持久化任务的版本管理](durable-tasks-versioning.md)。

<a id="combinators-and-handles"></a>

## 组合器与句柄

组合器并发调度其输入，因此运行时会一起提交这一批调用。被等待时，组合器作为等待它的执行实例的一部分运行，本身不记录结果；每个输入作为子任务运行，由组合器操作 ID 下的输入索引标识。使用 `WithId` 命名操作，以便后续部署增加或移除在它之前的组合器时，其输入仍能保持原有 ID。

每个组合器也是任务序列上的扩展方法，因此列表、数组或查询都可直接进行组合，例如 `lines.Select(line => Catalog(line.ProductId).ReserveAsync(OrderId, line.Units)).WhenAll().WithId("reserve")`。句柄列表也以相同方式组合，例如 `handles.WhenAny(cancellationToken)`。集合表达式自身没有类型，因此 `[a, b]` 应使用静态形式：`DurableTask.WhenAll([a, b])`。

- <xref:Orleans.Durable.Tasks.DurableTask.WhenAll*?displayProperty=nameWithType> 遵循 `Task.WhenAll` 的行为。它在所有输入进入终态后完成，包括持久化取消请求所影响的输入。随后，如果有失败，则按输入顺序抛出第一个失败；否则，如果有取消，则按输入顺序抛出第一个取消；否则，`WhenAll<TResult>` 按输入顺序返回结果。根据输入顺序选择异常，因此重放观察到相同的异常。
- <xref:Orleans.Durable.Tasks.DurableTask.WhenAny*?displayProperty=nameWithType> 返回最先完成的输入索引，`WhenAny<TResult>` 还返回该输入的结果。如果获胜输入失败或被取消，则组合器失败。其他输入继续运行。
- <xref:Orleans.Durable.Tasks.DurableTask.Race*?displayProperty=nameWithType> 像 `WhenAny` 一样选择获胜输入并返回相同的值，但还以 `Another task won the race.` 为原因请求取消其余所有输入，并等待它们全部进入终态，因此没有输入会在竞速结束后继续运行。获胜输入失败或被取消会使组合器失败；其他输入的结果不会使组合器失败。
- <xref:Orleans.Durable.Tasks.DurableTaskExtensions.WaitAsync*> 和 <xref:Orleans.Durable.Tasks.DurableTaskExtensions.WaitUntilAsync*> 将任务（输入 `0`）与持久化延迟（输入 `1`）进行竞速。如果任务首先完成，组合器取消延迟并返回任务结果。如果延迟先到期，任务会收到取消请求，取消原因指出发生了超时，组合器则等待该任务的结果：任务最终被取消时，组合器以 <xref:System.TimeoutException> 失败；否则，任务的其他结果（例如任务仍然返回的结果）就是组合器的结果。
- <xref:Orleans.Durable.Tasks.DurableTask.ScheduleAll*?displayProperty=nameWithType> 调度所有输入，并按输入顺序返回其 <xref:Orleans.Durable.Tasks.ScheduledTask> 句柄，无需等待结果。
- <xref:Orleans.Durable.Tasks.DurableTask.WhenAllCompleted*?displayProperty=nameWithType> 等待所有输入进入终态并返回句柄。某个输入失败或被取消不影响组合器成功完成，因此工作流可根据每个输入的结果采取行动，例如仅补偿成功的输入。
- <xref:Orleans.Durable.Tasks.DurableTask.WhenEach*?displayProperty=nameWithType> 调度其输入，并按完成顺序以 `IAsyncEnumerable` 的形式逐个产生句柄。失败或被取消的输入与成功输入一样会被返回。

Grain 在记录每个子任务终态结果的日志写入中，为其分配持久化的完成序号。`WhenAny`、`Race` 和 `WhenEach` 选择剩余输入中序号最小者，因此每次重放都选择相同的获胜者，并以相同顺序返回输入，即使重放时已有更多输入完成也是如此。`ScheduleAll`、`WhenAllCompleted` 和 `WhenEach` 只能在持久化执行实例内部被等待时运行，因为它们的句柄属于该执行实例。

<xref:Orleans.Durable.Tasks.ScheduledTask> 句柄观察一个任务。一旦等待、轮询或组合器观察到任务的终态响应，`IsCompleted`、`Status` 和 `Exception` 就会同步报告该响应。<xref:Orleans.Durable.Tasks.ScheduledTask`1.GetResult*> 返回结果或抛出已记录的失败或取消异常，<xref:Orleans.Durable.Tasks.ScheduledTask`1.TryGetResult*> 对成功完成的任务返回 `true` 并提供其结果。在句柄观察到终态响应之前，`Status` 为 `Pending`。在持久化任务内部，句柄的 `Id` 标识任务操作，供组合器和重放寻址；<xref:Orleans.Durable.Tasks.ScheduledTask.TargetTaskId> 则标识保存该任务的 Grain 上供其他参与方寻址的任务，即分离任务的根 ID、`GetDurableTask` 所观察的任务或调用在目标 Grain 上的任务。对于现有句柄，<xref:Orleans.Durable.Tasks.ScheduledTask.WhenAll*?displayProperty=nameWithType>、<xref:Orleans.Durable.Tasks.ScheduledTask.WhenAllCompleted*?displayProperty=nameWithType> 和 <xref:Orleans.Durable.Tasks.ScheduledTask.WhenEach*?displayProperty=nameWithType> 提供相同语义，<xref:Orleans.Durable.Tasks.ScheduledTask.Race*?displayProperty=nameWithType> 在每个句柄都观察到自身结果后返回获胜句柄。句柄上带超时的 `WaitAsync` 和 `WaitUntilAsync` 遵循组合器的超时规则；在持久化任务之外（例如客户端），使用系统时钟计量超时。`WhenEach` 不接受 `WithId`，因此要使用命名形式，应先通过 `ScheduleAll(...).WithId(name)` 调度输入，再枚举 `ScheduledTask.WhenEach(handles)`。

<xref:Orleans.Durable.Tasks.DurableTask.Delay*?displayProperty=nameWithType> 和 <xref:Orleans.Durable.Tasks.DurableTask.DelayUntil*?displayProperty=nameWithType> 是按逻辑时间计量的持久化定时器。运行时记录延迟的到期时间，并向 Grain 发送定时恢复消息；Grain 的收件箱保留该消息，直到 Durable Jobs 时钟到达该到期时间。逻辑时间读取 Durable Jobs 时钟，因此到期时间和收件箱处于同一时间域。记录到期时间就是一项承诺：若重放请求不同的到期时间（例如配置的持续时间发生变更），仍保留已记录的时间并记录警告。超时使用这些延迟，因此从首次等待该超时的运行开始计时。

<a id="cancellation"></a>

## 取消

取消是协作式的、持久化的且幂等的。Grain 通过 `IDurableTaskManager.CancelAsync(rootId, reason)` 请求取消自身的任务，客户端和其他调用方通过 <xref:Orleans.Durable.Tasks.ScheduledTask.CancelAsync*?displayProperty=nameWithType> 请求取消。如果请求取消的任务未被 Grain 保留，则抛出 `DurableTaskNotFoundException`，且不产生任何变更。请求在沿任务执行树传播之前提交：

- 延迟、方法体尚未启动的子任务，以及被取消的任务新调度的子任务，都以取消状态终止，而不执行其工作。
- 正在运行的子任务通过 <xref:Orleans.Durable.Tasks.DurableTaskContext.CancellationToken?displayProperty=nameWithType> 观察取消请求；对其他 Grain 尚未完成的调用也会收到取消请求，由被调用方将请求应用于自己的任务。
- 任务的方法体决定其最终结果。等待已取消的子任务会抛出 `OperationCanceledException`。当该异常传播出方法体时，任务被取消；如果方法体捕获该异常，则以自己的结果或失败结束。
- 已完成的子任务保留其已记录结果；重放已取消的任务时，会在与被重放的那次运行相同的位置观察取消请求，因此方法体采用相同的执行路径。该位置是方法体某个持久化操作的起点，例如启动子任务、`DurableTask.RunInline` 效果，或等待任务或信号：正在运行的方法体在当前操作处观察到请求，如果方法体正在进行持久化等待，该位置就是该等待操作，运行时将该位置与任务一起记录。重放时，在到达已记录位置之前，持久化取消令牌保持未取消。如果前一次运行中的取消位置未提交，则重放在到达前一次运行所记录的最后一个操作时观察取消请求，并记录该位置。如果重放在到达其已记录位置之前结束，就会因重放分歧而[陷入停滞](durable-tasks-versioning.md#stalled-executions)。

首次请求的取消原因会持久化：<xref:Orleans.Durable.Tasks.DurableTaskContext.TaskCancellationReason?displayProperty=nameWithType> 在已取消的执行实例、其后代和每次重放中返回该原因，取消请求还会将原因传递给对其他 Grain 尚未完成的调用。

方法体通过 <xref:Orleans.Durable.Tasks.DurableTaskExtensions.Shielded*> 启动补偿，以内联执行补偿操作。受屏蔽的子任务（例如本地步骤、延迟或对其他 Grain 的调用）即使其父任务已请求取消也会运行至完成，后续取消请求也会跳过它，但方法体仍拥有该子任务并等待其结果。受屏蔽的组合器会屏蔽它启动的每个输入。每个被等待的操作都遵循同样的屏蔽规则，因此受屏蔽的信号等待、延迟、`GetDurableTask` 订阅、`Race`、`WhenAny` 或 `WaitAsync` 都能在任务取消后完成。因停用、中止或删除任务执行树而发生的执行尝试取消，仍会终止受屏蔽的操作。

通过 `IDurableTaskManager.AbortAsync(rootId, reason)` 或 <xref:Orleans.Durable.Tasks.ScheduledTask.AbortAsync*?displayProperty=nameWithType> 执行管理性中止时，会在一次日志写入中将任务及其执行树以 <xref:Orleans.Durable.Tasks.DurableTaskAbortedException> 提交为失败，取消其正在运行的执行尝试，并向每个对其他 Grain 尚未完成的调用发送中止请求。已取消的执行尝试中的持久化操作会抛出 <xref:Orleans.Durable.Tasks.DurableTaskAttemptCanceledException>，而 `IsTaskCancellationRequested` 保持为 `false`，因此中止会在不执行补偿的情况下结束任务。适用于方法体忽略取消请求或清理工作陷入停滞的任务。

任务取消和执行尝试取消所记录的内容不同，请参阅[任务取消与执行尝试取消](durable-tasks-structured-concurrency.md#task-cancellation-and-attempt-cancellation)。

<a id="starting-and-observing-tasks"></a>

## 启动与观察任务

持久化调用的配置决定任务的性质，启动任务的调用方式决定调用方何时继续执行：

| | `await call` | `await call.ScheduleAsync()` | `call.Post()` |
| --- | --- | --- | --- |
| 附接（默认） | 运行子任务，结果持久化后返回该结果。 | 在子任务所在的 Grain 接纳并保存该任务后，返回其句柄。 | 将子任务暂存到当前执行实例的下一次日志写入，并返回其句柄。 |
| `.Shielded()` | 与附接任务相同，但调用方的取消请求不会传播给子任务。 | 与附接任务相同，但调用方的取消请求不会传播给子任务。 | 与附接任务相同，但调用方的取消请求不会传播给子任务。 |
| `.Detached()` | 启动独立的根任务并返回其结果。 | 在根任务所在的 Grain 接纳并保存该任务后，返回其句柄。 | 将根任务暂存到调用方的下一次日志写入，并返回其句柄。 |

- 附接子任务属于当前运行的执行树：执行实例拥有它，取消请求会传播给它，删除该树时也会删除它。
- <xref:Orleans.Durable.Tasks.DurableTaskExtensions.Detached*> 在目标上启动独立的根任务，用于后续工作、需要在启动方结束后继续运行的清理工作，以及启动方不需要结果的调用。启动方可以在该任务仍然运行时完成；启动方被取消、中止或删除时，该任务仍继续运行。任务的根 ID 就是 <xref:Orleans.Durable.Tasks.DurableTaskExtensions.WithId*> 指定的名称（原样使用），因此其他参与方可以使用此名称寻址；若未指定名称，根 ID 为 `{grainId}/{taskId}:{segment}`，其中 `{grainId}` 是启动方 Grain，`{segment}` 是执行实例下一个未命名操作的自动生成 ID，因此不同 Grain 上拥有相同任务 ID 的工作流会启动不同任务。共享任务的启动方应为其命名。启动操作会将执行实例登记为订阅者，因此等待该任务或其句柄可以收到结果；取消该等待只会结束订阅。
- <xref:Orleans.Durable.Tasks.ConfiguredDurableTask.ScheduleAsync*> 在目标 Grain 接纳并持久化保存任务后返回，因此可以将任务 ID（即句柄的 <xref:Orleans.Durable.Tasks.ScheduledTask.TargetTaskId>）交给其他参与方，通过 `GetDurableTask` 寻址该任务。目标的日志写入保存任务后，目标确认该调用，调用方发件箱将其退役。如果目标拒绝启动（因为任务 ID 已标识其他方法），会抛出 `DurableTaskConflictException`。其他接纳失败（例如目标无法解码请求）会重试；若启动操作未能在 Messaging 的 `OutboxRetryTimeout` 内被接纳，则抛出 `DurableTaskDeliveryException`。等待任务时也会抛出同样的失败。请参阅[死信](durable-tasks-operations.md#dead-letters)。
- <xref:Orleans.Durable.Tasks.ConfiguredDurableTask.Post*> 是同步操作。它将调用暂存到调用方发件箱，与调用代码所做的状态变更一起随 Grain 的下一次日志写入提交，并将其投递一次。目标在调用投递后才拥有该任务，因此如果其他参与方需要通过 ID 查找任务，应使用 `ScheduleAsync` 启动。在持久化任务内部，启动操作像 `DurableTask.RunInline` 效果一样被记录，因此重放返回相同的句柄，随后等待句柄会观察到结果。客户端没有发件箱，因此客户端上的 `Post` 会抛出 `InvalidOperationException`，并由 [ORLEANS0216](../diagnostics/orleans0216.md) 报告该问题。
- 在持久化任务内部，分离任务的记录 `$detach:{target}:{rootId}` 与启动操作一起提交，因此重放到达该记录时不会启动任何新任务。启动方保留携带请求的调用，直到目标接纳它，因此即使启动方的执行树被删除，该启动操作仍能存续。再次使用相同根 ID 启动相同方法不会改变任务。
- 在持久化任务之外，每个任务都是根任务，因此 `.Detached()` 不会改变调用。`WithId` 提供根 ID，使启动操作可安全重试：调用方在超时等不确定失败后重试启动操作，会找到同一个任务。如果启动操作未使用 `WithId`，则获得新的随机根 ID（通过句柄的 `Id` 报告），行为与普通调用相同。

结账操作在同一次日志写入中清空购物篮并启动订单工作流：

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_checkout_post" language="csharp":::

<a id="signals"></a>

## 信号

<xref:Orleans.Durable.Tasks.DurableSignal`1> 将外部事件（例如发货通知或审批）传递给正在运行的任务。发送方和等待方共享一个为信号命名的描述符：

- <xref:Orleans.Durable.Tasks.DurableSignal`1.WaitAsync*> 等待信号值。<xref:Orleans.Durable.Tasks.DurableSignal`1.TryWaitAsync*> 将等待操作与截止时间进行竞速，返回 `(true, value)` 或 `(false, default)`，并在每次重放时返回相同结果。
- 信号寻址的是*被调用任务（invoked task）*，即由客户端、另一个 Grain 或将其分离启动的持久化任务在 Grain 上启动的任务。该任务执行树中任意位置的等待操作均可观察到它的信号。
- 任务记录每个信号名称的第一个值，并向所有等待该名称的操作（包括重放期间的等待）返回该值。提前到达的信号会被记录，等待操作会立即返回该值。重复发送值相等的信号不会改变任务，值不同的信号会被拒绝。<xref:Orleans.Durable.Tasks.DurableSignal`1.For*> 为重复事件的每次发生派生不同的名称，例如 `approved/round-2`。
- Grain 通过 `IDurableTaskManager.SignalAsync` 或 `TrySignalAsync` 向自身任务发送信号，信号与 Grain 尚待提交的状态变更一起提交。客户端和其他 Grain 的普通方法使用 Grain 引用扩展方法 `SignalDurableTaskAsync` 和 `TrySignalDurableTaskAsync`，其他 Grain 上的持久化任务使用 `SignalDurableTaskAsync`，其发送操作会以 `$send:{target}:{taskId}:{name}` 为标识进行记录，供重放使用。同一执行实例中重复发送某个信号（例如在 `TaskNotFound` 后重试）会按出现顺序编号，例如 `$send:...$2`，因此每次都会再次发送。`Try` 形式返回 `DurableSignalResult`：`Accepted`、`Duplicate`、`Conflict`、`TaskNotFound` 或 `TaskCompleted`。
- Grain 可以在同一次日志写入中提交对另一个 Grain 的调用及发给该调用的信号。因此信号消息可能先到达；接收方 Grain 会记录信号值，而调用到达时便能观察到该值。

<a id="await-tasks-which-another-caller-started"></a>

## 等待由其他调用方启动的任务

Grain 引用扩展方法 `GetDurableTask<TResult>(rootId)` 或其 `TaskId` 重载，返回指向现有任务（例如客户端调度的操作或分离任务）的 <xref:Orleans.Durable.Tasks.DurableTaskReference`1>。持久化任务通过等待该引用来订阅任务结果，其他代码则通过等待该引用来等待结果。该引用通过扩展方法支持等待，因此 ASP.NET Core Minimal API 处理器将其作为值返回；<xref:Orleans.Durable.Tasks.IDurableTaskReference> 可以在不依赖结果类型的情况下读取其 `Target` 和 `Id`。例如，Webhook 中继等待操作完成，然后投递其结果：

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_reference_relay" language="csharp":::

- 在持久化任务内部，等待或调度该引用，或将其传给 `WhenAny`、`Race`、`WaitAsync` 或 `WaitUntilAsync`，都会使执行实例的一个子任务订阅它，并以该操作分配的子任务 ID 记录，类似于对其他 Grain 的调用。子任务及其订阅在同一次日志写入中提交，重放到达该子任务时不会再次订阅。
- 订阅通过发件箱作为不含请求载荷的调用传递给任务所在的 Grain，既不会运行任务，也不会取得任务的所有权。Grain 将子任务与任务的调用方一起加入任务的订阅者列表，通过自身发件箱向其发送任务结果（与向调用方发送结果相同），并保留任务，直到该完成通知退役。子任务以任务的最终结果（成功结果、失败或取消）完成。
- 订阅已处于终态的任务会迅速完成。如果 Grain 未将任务作为被调用任务保留（因为其保留期结束或从未被调用），订阅会迅速以 `DurableTaskNotFoundException` 失败；重放观察到相同的失败。传给订阅方的任务 ID 指向其 Grain 已持有的任务，因为 `ScheduleAsync` 在 Grain 接纳任务后才返回。可使用 `WaitAsync` 或 `WaitUntilAsync` 限制等待时间。
- 取消子任务（例如 `WaitAsync` 截止时间到达或某个 `Race` 输入在竞速中落败）会使其以取消状态结束，并结束其订阅。被订阅的任务继续运行。
- 对同一 Grain 内任务的订阅，在保存该任务结果的同一次日志写入中完成。
- 客户端和普通 Grain 方法等待同一引用，或通过向任务所在 Grain 发起调用，使用该引用轮询、取消或中止任务。

<a id="store-and-send-task-addresses"></a>

### 存储与发送任务地址

<xref:Orleans.Durable.Tasks.DurableTaskAddress> 将任务所在 Grain 与任务 ID 配对，使代码能够在日志化状态中保存任务地址、在 Grain 调用中传递地址或在 HTTP 有效载荷中返回地址，之后再访问该任务。Orleans 对其进行序列化；System.Text.Json 在 JSON 日志及 HTTP 有效载荷中将其写为 `{"Target":"order/42","Id":"place-order"}`，无需注册。<xref:Orleans.Durable.Tasks.TaskId> 序列化为经过转义的路径，例如 `place-order/reserve`。

- `GrainFactory.GetDurableTask<TResult>(address)` 或 `IClusterClient` 上的同名方法，返回任务引用，可按前文所述等待、订阅、轮询、取消或中止任务。
- `DurableTaskReference<TResult>.Address` 读取引用的地址。
- `handle.TryGetAddress(out var address)` 读取 <xref:Orleans.Durable.Tasks.ScheduledTask> 句柄所对应任务的地址：由客户端或普通 Grain 方法启动的根任务，或者持久化任务内部的分离任务或 `GetDurableTask` 所观察的任务。附接子任务属于其调用方的执行树，因此它的句柄没有地址。

Grain 记录其交接的分离任务，并可在之后经历任意次数的重新激活后等待、轮询或取消该任务：

:::code source="../snippets/compiled-durable/Grains/DurableTaskIdentitySnippets.cs" id="durable_tasks_identity_store" language="csharp":::

[任务标识与引用](durable-tasks-identity.md#address-a-task-from-elsewhere)介绍了哪些任务具有地址，以及这些地址在多长时间内可解析到任务。

<a id="commit-model-and-guarantees"></a>

## 提交模型与保证

- `(grain, TaskId)` 标识一个执行实例。当复用任务 ID 的请求来自同一调用方且调用同一接口方法时，视为同一请求；不比较参数。复用 ID 调用不同接口或方法会发生冲突。
- 在执行开始前提交请求标识、任务状态、调用方订阅和收件箱处理完成状态。
- 任务状态、发出消息与 Grain 自身的日志化状态共享 Grain 的日志提交边界。日志写入失败会使 Grain 停用，下一个激活实例从已提交状态恢复。
- 执行实例的每个执行片段（两次持久化等待之间的代码）作为 Grain 上的一个请求运行，因此与 Grain 方法一样遵循 Grain 的可重入性规则。
- 当执行片段正在运行或等待运行时，Grain 保留一个 Durable Jobs 唤醒作业，因此发生崩溃的 Grain 会在大约 30 秒内重新激活并恢复执行实例。等待调用结果、信号或延迟的执行实例，则在其等待的消息重新激活 Grain 时恢复执行。
- 从持久化 Grain 投递持久化调用（包括从普通方法发起）会将调用暂存到发件箱，使其与 Grain 的状态变更原子提交。在持久化任务内部，等待和调度调用也以同样的方式暂存调用。
- 每个请求与结果的有效载荷在每个 Grain 上仅在任务状态中记录一次。转为死信的完成通知也会在死信中保存结果，以供检查和重放。
- 在启动新的执行之前先进行恢复。子任务 ID、完成顺序和发出的消息在重放时保持稳定。
- 终态结果和发送给调用方的完成通知一起提交。在任务完成与取消的竞速中，第一个持久化的终态结果胜出。
- 方法体的结果暂存于激活实例上运行方法体最终代码的工作项中，因此无论由哪个操作请求写入，结果均与该代码修改的状态在同一次日志写入中提交。`DurableTask.RunInline` 的记录与效果的状态变更暂存于同一工作项中，并一同提交。

持久化保证覆盖参与 Orleans Journaling 与 Messaging 提交的状态和消息。调用支付提供程序等外部副作用需要自行保证幂等性，例如使用根据任务 ID 派生的请求键；请参阅[持久化任务中的幂等性](durable-tasks-idempotency.md)。

<a id="lifecycle-and-retention"></a>

## 生命周期与保留期

*被调用任务*（由客户端或其他 Grain 调度）拥有一棵执行树：任务自身及那些本身并非被调用任务的后代，例如本地步骤、延迟、`DurableTask.RunInline` 记录、分离任务的记录、信号发送标记、订阅及对其他 Grain 的调用记录。被调用任务进入终态后，其所在 Grain 在以下情况下删除执行树：

- 如果任务由 Grain 调用，则在调用方 Grain 已持久化接纳结果之后、使完成通知退役的那次日志写入中删除。
- 如果任务没有调用方 Grain，例如客户端调度的任务或分离任务，则在任务完成后的 `DurableTaskOptions.ResultRetentionPeriod` 到期时删除。在该期间进行轮询会返回保留的结果，之后的轮询会抛出 `DurableTaskNotFoundException`。
- 无论哪种情况，都必须等到发给各订阅持久化任务的完成通知退役后才能删除。

删除执行树会取消其中尚未完成的任务。因此任务 ID 仅在任务删除前对调度请求去重；删除后再次调度同一 ID 会重新运行请求。对于已经删除的任务，随后到达的完成通知和延迟恢复消息将被忽略。请参阅[保留与删除](durable-tasks-operations.md#retention-and-deletion)了解运维细节。

<a id="configure-the-silo-and-clients"></a>

## 配置 Silo 和客户端

持久化任务需要 Journaling 存储和 Durable Jobs 存储。Silo 构建器上的 `AddDurableTasks` 添加 Messaging 和持久化任务运行时，将任务状态保存在 Grain 的日志中，并注册 JSON 日志格式处理任务状态所需的 JSON 元数据。它的可选回调配置 `DurableTaskOptions`。在开发环境中可配置内存存储：

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_registration" language="csharp":::

需要调度、观察、取消持久化任务或向其发送信号的外部客户端，应注册客户端适配器：

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_client_registration" language="csharp":::

继承自 <xref:Orleans.Durable.Journaling.DurableGrain> 的 Grain 承载持久化任务，`AddDurableTasks` 为每个这样的 Grain 提供收件箱和发件箱。在激活设置阶段、日志恢复之前，运行时绑定到 Grain 的日志、收件箱和发件箱；Grain 可以注入 `IDurableTaskManager`，用于观察、取消、中止自身任务以及向它们发送信号。任务状态可采用任意日志格式；请求和结果通过 Orleans 序列化器保存。多 Silo 部署应使用共享的、生产级的 Journaling 和 Durable Jobs 提供程序；请参阅[Journaling 配置](journaling/configuration.md)。
