---
title: 持久化任务实践指南
description: 并发扇出、竞速、超时、补偿、等待所有输入进入终态、按完成顺序处理结果、等待信号、启动分离任务，以及取消、中止和调度持久化任务。
ms.date: 10/14/2026
ms.topic: how-to
---

<a id="durable-task-recipes"></a>

# 持久化任务实践指南

这些可编译的示例使用派生自 <xref:Orleans.Durable.Journaling.DurableGrain> 的 Grain，以及[持久化任务](durable-tasks.md#define-durable-task-methods)中的契约。按照[配置 Silo 和客户端](durable-tasks.md#configure-the-silo-and-clients)中的说明配置 Journaling、Durable Jobs 和持久化任务运行时。每个示例的结果均可通过[轮询任务](durable-tasks-operations.md#observe-a-task)以及 `DurableTaskEvents` 报告的[运行时状态转换](durable-tasks-operations.md#observe-runtime-transitions)进行观察。

<a id="fan-out-to-every-line-with-a-named-whenall"></a>

## 使用命名的 WhenAll 并发处理每个订单行

**任务：** 并发为每个订单行预留库存，并按订单行顺序继续处理各行的结果。

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_fan_out" language="csharp":::

<xref:Orleans.Durable.Tasks.DurableTask.WhenAll*?displayProperty=nameWithType> 在一个批次中为每行调度一次库存预留，订单 Grain 通过一次日志写入提交该批次，并按输入顺序返回一个 `bool[]`。即使收到取消请求，它也会等待所有预留操作结束，因此 `catch` 块开始执行时，已没有仍在运行的预留操作。`WithId("reserve")` 将输入标识为 `reserve/0`、`reserve/1`，依此类推。

注意事项：

- 按确定性顺序构造输入，例如订单的行顺序。输入索引是各输入任务 ID 的一部分。
- 为组合器命名。未命名组合器生成的 ID 取决于此前运行的组合器数量，因此在后续部署中于其之前添加一个组合器，会使其输入的编号发生变化。
- `WhenAll` 优先抛出按输入顺序遇到的第一个失败，否则抛出第一个取消。若要分别处理各输入的结果，请使用 [`WhenAllCompleted`](#settle-every-input-and-inspect-each-outcome)。

**验证：** 订单 Grain 列出从 `place-order-{orderId}/reserve/0` 到 `reserve/{n-1}` 的任务，每个任务均为 `Completed`，结果为 `True` 或 `False`；各商品目录 Grain 中也列出具有相同 ID 的任务。

<a id="take-the-first-result-and-cancel-the-rest-with-race"></a>

## 使用 Race 获取首个结果并取消其余请求

**任务：** 从两家承运商中取得最先返回的运输报价，并取消另一家承运商的请求。

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_race" language="csharp":::

<xref:Orleans.Durable.Tasks.DurableTask.Race*?displayProperty=nameWithType> 将请求调度为 `first-quote/0` 和 `first-quote/1`，并选出最先完成的请求。Grain 为各输入的结果标记持久化完成序号，因此即使另一请求在此后也已完成，重放仍会选出相同的获胜者。随后 `Race` 以 `Another task won the race.` 为原因请求取消另一请求，并等待该请求进入终态，因此只有两家承运商的调用均已结束后，工作流才会继续。`Race<TResult>` 返回获胜者的索引和结果。

注意事项：

- 获胜者失败或被取消都会使 `Race` 失败。若某输入的失败属于预期结果，应从该输入返回值，例如 `null`。
- 取消是协作式的。若承运商忽略取消请求，竞速操作会一直等待，直到该调用完成，并丢弃该调用的结果。
- 若要让其余输入继续运行，请使用 <xref:Orleans.Durable.Tasks.DurableTask.WhenAny*?displayProperty=nameWithType>，它的其他输入会继续运行，直到自身完成或运行时删除任务的执行树。

**验证：** 较慢的请求尚未进入终态时，运输 Grain 将其列为 `Canceling`，原因为 `Another task won the race.`；`QuoteFirstAsync` 返回后，`first-quote/0` 和 `first-quote/1` 均已处于终态。

<a id="time-out-a-call-with-waitasync"></a>

## 使用 WaitAsync 为调用设置超时

**任务：** 向陆运承运商请求报价，两分钟后放弃等待。

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_timeout" language="csharp":::

<xref:Orleans.Durable.Tasks.DurableTaskExtensions.WaitAsync*?displayProperty=nameWithType> 使调用 `ground-quote/0` 与持久化延迟 `ground-quote/1` 竞速。运行时记录延迟的到期时间，因此 Grain 停用后的重放仍保留原截止时间：超时从首次运行时开始计算。当延迟先到期时，调用会收到取消请求，原因为 `The operation timed out after 00:02:00.`，而 `WaitAsync` 会等待承运商的调用结果。如果调用最终被取消，则抛出 <xref:System.TimeoutException>，其内部异常为该调用的 `OperationCanceledException`；若承运商仍然返回了报价，则以该报价为结果。当调用先完成时，`WaitAsync` 取消延迟，并返回调用结果或抛出其失败异常。<xref:Orleans.Durable.Tasks.DurableTaskExtensions.WaitUntilAsync*> 则接受绝对截止时间。

注意事项：

- 根据 <xref:Orleans.Durable.Tasks.DurableTaskContext.UtcNow?displayProperty=nameWithType> 计算绝对截止时间，使重放计算出相同的截止时间。
- 捕获 `TimeoutException` 以处理超时。`OperationCanceledException` 表示其他取消情况，例如工作流自身的取消。
- `Race(...).WaitAsync(timeout).WithId(name)` 限制整个竞速操作的时长，该操作随后作为子操作 `{name}/0` 运行，其输入归属于该子操作。
- 对于已经调度的任务，带超时参数的 <xref:Orleans.Durable.Tasks.ScheduledTask.WaitAsync*?displayProperty=nameWithType> 适用相同规则。在持久化任务外部（例如客户端），它使用系统时钟计算超时。

**验证：** 运输 Grain 将 `ground-quote/1` 列为 `Delayed`，直到其到期。超时后，`ground-quote/0` 报告原因为 `The operation timed out after 00:02:00.`，承运商的请求最终被取消时，工作流返回 `null`。

<a id="compensate-inline-when-the-workflow-is-canceled"></a>

## 工作流取消时在原工作流内执行补偿

**任务：** 当买家在验证期间取消订单时，释放所有已预留的库存，并记录取消订单的原因。

[并发扇出示例](#fan-out-to-every-line-with-a-named-whenall)中的 `catch` 块通过 `IsTaskCancellationRequested` 进行筛选，因此它处理的是工作流自身任务的取消，并将 <xref:Orleans.Durable.Tasks.DurableTaskContext.TaskCancellationReason?displayProperty=nameWithType> 传递给补偿操作：

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_compensation" language="csharp":::

已取消的任务启动子操作时，子操作会直接以已取消状态终止而不执行，除非任务通过 <xref:Orleans.Durable.Tasks.DurableTaskExtensions.Shielded*> 启动它们。受取消屏蔽保护的 `release` 组合器会保护其每个输入，因此输入和 `cancel` 效果操作会运行至完成，工作流返回记录取消原因的 `OrderResult`。从未执行的库存预留不会留下需要释放的内容，因此 `ReleaseAsync` 返回订单是否曾持有预留库存，可以安全地对所有订单行执行释放操作：

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_catalog_grain" language="csharp":::

注意事项：

- 为每个补偿子操作提供取消屏蔽保护。未受保护的子操作会直接以已取消状态终止，不会运行。
- 使补偿操作具有幂等性，并报告是否实际执行了补偿动作，这样工作流无须知道哪些步骤已运行即可进行补偿。
- 中止不会运行补偿操作。中止适用于补偿操作卡住的任务。
- 必须比工作流存续更久的工作（例如投递工作流结果后的清理操作）应放在[分离任务](#start-follow-up-work-without-waiting)中。

**验证：** 在宽限期内调用 `CancelAsync` 后，订单 Grain 将 `grace-period` 列为 `Canceled`，将 `release/{n}` 和 `cancel` 列为 `Completed`，并将根任务列为 `Completed`，其结果为描述内容是 `Cancelled by the buyer.` 的 `OrderResult`。

<a id="settle-every-input-and-inspect-each-outcome"></a>

## 等待所有输入进入终态并检查各自结果

**任务：** 为每个订单行预留库存；当任一预留失败、遭拒绝或被取消时，仅释放预留成功的订单行。

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_when_all_completed" language="csharp":::

<xref:Orleans.Durable.Tasks.DurableTask.WhenAllCompleted*?displayProperty=nameWithType> 调度所有输入，等待每个输入进入终态，并按输入顺序返回它们的句柄。输入失败或被取消不会导致组合器失败，即使工作流自身发出了取消请求且该请求传播到了各输入，组合器仍会成功完成。每个句柄均已观察到其结果，因此 <xref:Orleans.Durable.Tasks.ScheduledTask`1.TryGetResult*> 对于成功完成的预留操作会返回 `true` 并提供结果，而 <xref:Orleans.Durable.Tasks.ScheduledTask.Status>、<xref:Orleans.Durable.Tasks.ScheduledTask.Exception> 和 <xref:Orleans.Durable.Tasks.ScheduledTask`1.GetResult*> 可用于检查其他结果。工作流使用受取消屏蔽保护的组合器释放已持有的库存，因此即使工作流已被取消，释放操作也会执行；随后重新抛出取消异常，使任务以已取消状态结束。

注意事项：

- 根据各句柄的结果进行分支，而不是依据组合后的结果：组合器不会因某个输入而抛出异常。
- 只要工作流可能在补偿期间被取消，就应为释放操作提供取消屏蔽保护。
- `WhenAllCompleted` 只有在持久化执行内部被等待时才会运行。对于 `ScheduleAll` 先前返回的句柄，可用 <xref:Orleans.Durable.Tasks.ScheduledTask.WhenAllCompleted*?displayProperty=nameWithType> 以相同方式等待。

**验证：** 某订单行库存不足时，分配 Grain 会列出具有 `True` 和 `False` 结果的 `reserve/{i}`，并且只为每个结果为 `True` 的订单行列出一个 `release/{n}` 输入；各商品目录 Grain 的可用库存恢复至初始值。

<a id="process-results-as-they-arrive-with-wheneach"></a>

## 使用 WhenEach 按完成顺序处理结果

**任务：** 向多家承运商请求报价，按报价到达的顺序保留前 `count` 个报价，并取消其余请求。

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_when_each" language="csharp":::

<xref:Orleans.Durable.Tasks.DurableTask.ScheduleAll*?displayProperty=nameWithType> 将请求调度为 `quotes/0`、`quotes/1` 等，并返回它们的句柄。<xref:Orleans.Durable.Tasks.ScheduledTask.WhenEach*?displayProperty=nameWithType> 按完成顺序逐个产出每个句柄，且每个句柄只产出一次，包括失败或被取消的请求。运行时根据各结果的持久化完成序号选择下一个句柄，因此每次重放均以相同顺序产出句柄并运行相同的步骤，即使重放时已有更多请求完成。循环结束后，<xref:Orleans.Durable.Tasks.ScheduledTask.CancelAsync*?displayProperty=nameWithType> 按给定原因取消各请求；取消已完成的请求不会产生任何变更。

注意事项：

- 根据枚举已产出的句柄作出决策。尚未由枚举产出的句柄在重放时可能报告不同的 `IsCompleted` 值。
- `DurableTask.WhenEach` 不接受 `WithId`；若要命名输入，请等待 `ScheduleAll(...).WithId(name)`，再枚举 `ScheduledTask.WhenEach(handles)`。
- 枚举未遍历到的请求会继续运行，直到完成、被取消或任务的执行树被删除。

**验证：** 运输 Grain 为每家承运商列出 `quotes/{i}`。尚未完成的各请求在处于 `Canceling` 状态时报告原因为 `Enough quotes were collected.`，随后报告其承运商 Grain 所报告的结果。

<a id="schedule-now-await-later-with-scheduleall"></a>

## 使用 ScheduleAll 先调度、后等待

**任务：** 启动所有包裹标签的打印操作，在打印标签期间记录订单已完成拣货，随后收集标签。

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_schedule_all" language="csharp":::

`ScheduleAll(...).WithId("labels")` 通过一次日志写入提交所有标签请求，并在不等待完成的情况下返回其句柄。打印标签期间运行 `pick` 步骤。随后 <xref:Orleans.Durable.Tasks.ScheduledTask.WhenAll*?displayProperty=nameWithType> 等待所有标签，优先抛出按输入顺序遇到的第一个失败，否则抛出第一个取消；成功时，各句柄的 `GetResult()` 返回对应标签。

注意事项：

- 在工作流返回前等待每个句柄，或将它传递给组合器。工作流未等待的标签任务会继续运行，运行时会在删除任务的执行树时将其取消。
- `ScheduleAll` 只有在持久化执行内部被等待时才会运行，因为其句柄归属于该执行。

**验证：** 履约 Grain 列出 `labels/{i}` 和 `pick`，标签任务相对于 `pick` 可以按任意顺序完成。

<a id="wait-for-an-external-event-with-a-deadline"></a>

## 在截止时间前等待外部事件

**任务：** 对于一份费用报销申请，最多等待三天以获取审批人的决定。

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_signal" language="csharp":::

<xref:Orleans.Durable.Tasks.DurableSignal`1.TryWaitAsync*> 使信号与到截止时间为止的持久化延迟竞速，并返回 `(true, decision)` 或 `(false, default)`。任务记录首次收到的决定，每次等待和重放均返回该决定。普通 Grain 方法通过 `IDurableTaskManager.TrySignalAsync` 投递决定，该投递操作与 Grain 待提交的状态变更一同提交，并返回 `DurableSignalResult`。客户端通过 Grain 引用发送相同的信号：

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_signal_client" language="csharp":::

注意事项：

- 根据 <xref:Orleans.Durable.Tasks.DurableTaskContext.UtcNow?displayProperty=nameWithType> 计算截止时间，使重放计算出相同的截止时间。
- 以被调用的任务为目标，在本例中即客户端调度的根任务。本地子操作的 ID 并不标识被调用的任务，因此向其发送信号会返回 `TaskNotFound`。
- 每个名称仅记录一个值。使用 <xref:Orleans.Durable.Tasks.DurableSignal`1.For*> 为重复事件的每次发生派生一个名称，例如每轮审批使用一个名称。
- 在任务运行期间，截止时间之后到达的决定也会被记录，同一任务中后续的 `Decided.WaitAsync()` 将返回该决定。

**验证：** `DecideAsync` 返回 `Accepted`，再次提交相同决定返回 `Duplicate`，提交不同决定返回 `Conflict`，而在任务未收到决定便已完成之后提交决定则返回 `TaskCompleted`。

<a id="start-follow-up-work-without-waiting"></a>

## 无须等待即可启动后续工作

**任务：** 开具发票时发送收据，无须让发票工作流等待通知服务，也不会在发票被取消时取消收据发送。

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_detach" language="csharp":::

<xref:Orleans.Durable.Tasks.DurableTaskExtensions.Detached*> 使调用成为通知 Grain 上独立的根任务，<xref:Orleans.Durable.Tasks.DurableTaskExtensions.WithId*> 将其命名为 `receipt-{invoiceId}`，而 <xref:Orleans.Durable.Tasks.ConfiguredDurableTask.Post*> 将其暂存到发票 Grain 的发件箱中。调用及其在发票执行树中的记录与发票结果一同提交，因此重放到达此记录时不会再次启动任务。观察者根据收据任务的名称定位它：

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_detach_observe" language="csharp":::

注意事项：

- 根据稳定的值命名分离任务。在已有根 ID 下启动不同的方法，会像其他任务 ID 重用情况一样失败。
- 如果工作流在自身结果提交之前将收据任务 ID 交给另一方，应通过 `ScheduleAsync` 启动该任务；它会在通知 Grain 持有该任务后返回。
- 通知 Grain 在收据任务完成后将其结果保留 `ResultRetentionPeriod`，以供轮询。

**验证：** 通知 Grain 列出 `receipt-{invoiceId}`；发票 Grain 在发票任务下列出一条 `$detach:…` 记录，该记录根据通知 Grain 和收据任务的根 ID 命名，并保留至发票的执行树被删除。

<a id="cancel-and-abort-from-ordinary-grain-methods"></a>

## 从普通 Grain 方法取消和中止任务

**任务：** 允许买家在订单库存确认之前取消订单，并允许运维人员中止卡住的工作流。

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_order_controls" language="csharp":::

Grain 注入 `IDurableTaskManager`，它直接针对当前激活自身的运行时执行操作，无须发起 Grain 调用。`CancelAsync(rootId, reason)` 在请求已提交并传播后完成；工作流观察该请求并决定其结果，如[补偿示例](#compensate-inline-when-the-workflow-is-canceled)所示。`AbortAsync(rootId, reason)` 将工作流及其执行树以 <xref:Orleans.Durable.Tasks.DurableTaskAbortedException> 提交为失败状态，该异常的消息为中止原因，并向每个尚未完成的跨 Grain 调用发送中止请求。客户端使用任务句柄：

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_client_controls" language="csharp":::

注意事项：

- 在取消之前检查业务状态。若取消请求在工作流已经过可取消步骤之后到达，只会执行这些步骤仍能触达的补偿操作。
- 重复的取消请求是幂等的，工作流读取的是首次请求的原因。
- 中止不会执行补偿操作，而且分离任务会继续运行；须单独中止这些任务。

**验证：** 已取消订单的诊断信息将根任务报告为 `Completed`，并携带取消结果；或者在异常传播出工作流时，将根任务报告为 `Canceled`，响应为取消原因。被中止的根任务报告为 `Faulted`，并携带 `DurableTaskAbortedException: <reason>`。

<a id="schedule-a-workflow-from-another-grain-or-a-client"></a>

## 从其他 Grain 或客户端调度工作流

**任务：** 在买家结算时启动订单工作流，并观察其结果。

持久化 Grain 从普通方法通过 <xref:Orleans.Durable.Tasks.ConfiguredDurableTask.Post*> 启动工作流，该方法将调用暂存到其发件箱中，使购物篮的清空操作和调用在随后的一次日志写入中一同提交：

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_schedule_from_grain" language="csharp":::

该工作流是订单 Grain 上的根任务；订单 Grain 在任务完成后将任务及其结果保留 `ResultRetentionPeriod`，而购物篮 Grain 使用 Grain 引用扩展方法 `GetDurableTask<TResult>` 在订单 Grain 上轮询该任务。

客户端使用 <xref:Orleans.Durable.Tasks.ConfiguredDurableTask.ScheduleAsync*> 调度工作流，该方法在订单 Grain 持有任务后返回。客户端可等待返回的句柄，也可稍后使用 `GetDurableTask<TResult>` 根据根 ID 重新关联到任务：

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_schedule_from_client" language="csharp":::

<xref:Orleans.Durable.Tasks.PollingOptions> 值的 `PollTimeout` 为零时立即返回当前状态；更长的超时值则最多等待这么长时间以获取任务完成结果。在持久化任务内部，等待调用会将其作为子任务运行，而 `WithId(name).ScheduleAsync()` 启动一个子任务并返回其句柄，供稍后等待。

注意事项：

- 使用 `WithId` 根据业务标识（例如订单 ID）派生根 ID，使结算重试到达同一个任务，而非启动第二个工作流。
- 根任务在完成后保留 `ResultRetentionPeriod`。保留期结束后的轮询会抛出 `DurableTaskNotFoundException`，再次使用相同的根 ID 启动则会重新运行工作流。

**验证：** 订单 Grain 列出 `place-order-{orderId}`，购物篮 Grain 不会为该工作流列出任务，而 `GetOrderResultAsync` 在订单发货后返回结果。
