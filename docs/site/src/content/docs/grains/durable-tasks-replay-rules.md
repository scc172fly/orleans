---
title: 持久化任务的重放安全规则
description: 介绍保证持久化任务体在重放时正确执行的规则、各规则所防止的故障，以及实施这些规则的 ORLEANS0200 至 ORLEANS0223 分析器诊断。
ms.date: 10/16/2026
ms.topic: reference
---

<a id="replay-safety-rules-for-durable-tasks"></a>

# 持久化任务的重放安全规则

持久化任务在停用或故障后从头重新运行任务体，而每个已记录的操作会返回其已记录的结果，从而恢复执行。本文的规则确保这种重放与最初记录操作的那次运行等价。`Microsoft.Orleans.Durable.Abstractions` 包包含分析器，在构建时检查这些规则并报告 ORLEANS0200 至 ORLEANS0223 诊断；每个诊断页面展示触发诊断的代码及常见修复方式。

有关这些规则背后的模型，参见[重放与确定性](durable-tasks.md#replay-and-determinism)和[持久化任务中的幂等性](durable-tasks-idempotency.md)。

<a id="replayed-code-recorded-operations-and-the-tail"></a>

## 重放代码、已记录操作与尾部代码

分析器将每个持久化任务体划分为以下区域：

- **已记录的操作**在每个执行实例中运行一次，重放时返回它们的结果：等待持久化调用、本地步骤、`DurableTask.Run` 委托、延迟、信号等待、组合器及句柄；`DurableTask.RunInline` 效果；以及 `Post`。若等待的是同一项目中执行持久化操作、返回 `Task` 的辅助方法，该等待也算作一个已记录操作。
- **重放代码**是仍可到达某个已记录操作的代码。在后续操作已经提交其结果和状态变更之后，重放会再次运行这些代码。
- **同步尾部**是无法再到达任何已记录操作的代码。它只在完成任务体的那次运行中执行，其状态变更与任务结果一同提交。
- **已记录操作的任务体**，包括 `DurableTask.RunInline` 和 `DurableTask.Run` 接收的委托及本地步骤的任务体，每个已记录结果对应一次运行。步骤的任务体本身也是持久化任务体，拥有自己的各类区域。

`DurableTask.RunInline` 效果会在当前工作项中立即运行，然后返回；`DurableTask.Run` 步骤在被等待、调度或投递时作为子执行实例运行，可能发生在之后的执行轮次中。因此，同步代码块中连续的效果与其改变的状态在同一次日志写入中提交，其间不会运行该 Grain 的其他执行轮次；而每个步骤都有自己的 `DurableTask.Current`、取消作用域和日志写入。同名的效果和步骤共用一个标识符，因此将其中一种改为另一种，会导致已经记录过该操作的执行实例发生重放分歧；应使用 `DurableTask.GetVersion` 对变更进行分支。

如果重放与记录发生分歧，例如在已记录 ID 下调度了另一操作，或发现某个已记录结果已无法解码，运行时不会将该故障作为任务体中的异常继续执行，而是使用 `DurableTaskAttemptCancellationReason.Diverged` 使执行实例停滞，并保持任务处于待完成状态，直到与记录匹配的代码重放该任务。参见[诊断重放分歧](durable-tasks-operations.md#diagnose-replay-divergence)和[停滞的执行实例](durable-tasks-versioning.md#stalled-executions)。

下面的 Grain 展示了各个区域：

:::code source="../snippets/compiled-durable/Grains/DurableTaskSnippets.cs" id="durable_tasks_replay_regions" language="csharp":::

持久化任务体是返回 `DurableTask` 或 `DurableTask<TResult>` 的 `async` 方法、局部函数或 Lambda 表达式，或者传给 `DurableTask.Run` 的委托。普通 Grain 方法不是持久化任务体，这些规则不适用于它们。分析在过程内部进行：不会检查任务体调用但未等待的辅助方法，也不会检查其他程序集中的辅助方法。

<a id="rules"></a>

## 规则

| 规则 | 防止的故障 | 诊断 |
| --- | --- | --- |
| 在已记录效果、步骤或尾部代码中读取和修改 Grain 状态。 | 重放重复应用变更，或根据后续操作写入的状态作出决策。 | [ORLEANS0200](../diagnostics/orleans0200.md) |
| 在已记录效果、步骤或尾部代码中生成时钟、随机数和环境相关值，并通过 `DurableTask.Current.UtcNow` 读取时间。如果尾部代码要向外部服务发送某个值（如幂等键），应在已记录效果或步骤中生成它，因为结果尚未提交的尾部代码会再次运行。 | 重放计算出不同参数、ID、键或截止时间，导致分歧或重复执行外部副作用。 | [ORLEANS0201](../diagnostics/orleans0201.md) |
| 在步骤或尾部代码中等待外部操作。 | 重放重复操作、观察到不同结果，或在服务不可用时失败。 | [ORLEANS0202](../diagnostics/orleans0202.md) |
| 在步骤或尾部代码最后一次等待外部操作之后修改状态。 | 状态变更在结果提交之前已提交，下一次运行又重复应用该变更。 | [ORLEANS0203](../diagnostics/orleans0203.md) |
| 返回 `DurableTask.RunInline` 效果的输出。 | 重放跳过该效果，任务体中的变量仍保持初始值。 | [ORLEANS0204](../diagnostics/orleans0204.md) |
| 等待、调度或投递每个持久化任务定义。 | 任务从未运行。 | [ORLEANS0205](../diagnostics/orleans0205.md) |
| 每个 `async DurableTask` 定义只运行一次。 | 第二次运行抛出 `InvalidOperationException`。 | [ORLEANS0206](../diagnostics/orleans0206.md) |
| 操作名称中不得包含 `$`。 | 运行时拒绝该名称并抛出 `ArgumentException`。 | [ORLEANS0207](../diagnostics/orleans0207.md) |
| 为每个调用位置分配独立名称。 | 移除或重新排序一个调用位置会导致其他调用位置记录的 ID 偏移。 | [ORLEANS0208](../diagnostics/orleans0208.md) |
| 使任务体始终在激活实例的调度器上执行。 | 工作与 Grain 的执行发生竞态，且 `RunInline` 和 `Post` 在调度器之外抛出异常。 | [ORLEANS0209](../diagnostics/orleans0209.md) |
| 使用持久化组合器执行并发的持久化工作。 | 子任务按线程执行顺序获得 ID，重放可能改变该顺序；竞速失败的输入仍继续运行。 | [ORLEANS0210](../diagnostics/orleans0210.md) |
| 使用 `DurableTask.Delay` 和 `DelayUntil` 等待。 | 停用会丢失等待进度，重放又等待完整时长。 | [ORLEANS0211](../diagnostics/orleans0211.md) |
| 使用 `DurableTask.Current.IsTaskCancellationRequested` 过滤取消处理程序。 | 一次执行尝试的取消（例如停用）会触发补偿，即使任务本身并未被任何人取消。 | [ORLEANS0212](../diagnostics/orleans0212.md) |
| 任务体中的异步代码应返回任务。 | 工作在任务体完成后继续运行，其失败无法被任务体捕获。 | [ORLEANS0213](../diagnostics/orleans0213.md) |
| 每次重复等待信号时派生不同的信号名称。 | 每次等待都返回信号收到的第一个值。 | [ORLEANS0214](../diagnostics/orleans0214.md) |
| 重放代码通过 `logger.ReplaySafe()` 记录日志。 | 每次重放都重复记录已完成操作的事件。 | [ORLEANS0215](../diagnostics/orleans0215.md) |
| 从 Grain 和持久化任务体投递，从客户端调度。 | `Post` 抛出异常，因为客户端没有持久化发件箱。 | [ORLEANS0216](../diagnostics/orleans0216.md) |
| 任务取消后的清理操作发起调用时传入 `AttemptCancellationToken`。 | 因为 `CancellationToken` 已经取消，清理调用立即结束。 | [ORLEANS0217](../diagnostics/orleans0217.md) |
| 版本检查应传入常量名称及范围。 | 同一变更记录多个决策，或一次部署记录了任何分支都无法处理的版本。 | [ORLEANS0218](../diagnostics/orleans0218.md) |
| 等待、组合或返回每个已调度子任务的句柄。 | 子任务失败未被观察，删除执行实例的任务树时会取消该子任务。 | [ORLEANS0219](../diagnostics/orleans0219.md) |
| 为循环、条件中的操作及其他未命名操作之后的操作命名。 | 循环、条件或部署使生成的标识符偏移，导致重放分歧。 | [ORLEANS0220](../diagnostics/orleans0220.md) |
| 使用 `DurableTask.RunInline` 执行同步工作。 | 步骤没有执行异步工作，却额外增加了一个子执行实例及一次日志写入。 | [ORLEANS0221](../diagnostics/orleans0221.md) |
| 向 `DurableTask.Run` 和 `DurableTask.RunInline` 传入返回任务的委托。 | 任务将 `async void` 委托在其第一个 `await` 处记录为已完成，而实际工作尚未执行完。 | [ORLEANS0222](../diagnostics/orleans0222.md) |
| 为 Grain 状态赋予新的不可变值。 | 就地修改绕过日志，导致恢复出的状态与内存中的状态不一致。 | [ORLEANS0223](../diagnostics/orleans0223.md) |

<a id="state-values-and-external-work"></a>

### 状态、值及外部工作

前四项规则确保每个副作用只在一次运行中生效。Grain 状态包含所有已提交变更，因此重放代码会读取更新的状态，并重复应用其写入操作。`DurableTask.RunInline` 效果、步骤的最后部分代码和尾部代码都会将变更与记录一同提交，因此重放不会重复应用这些变更。外部调用在步骤中运行，重放返回步骤记录的结果；调用还会发送从任务派生的幂等键，因为结果尚未提交的步骤会再次运行。参见[将持久化任务与其他系统集成](durable-tasks-integrations.md)。

<a id="definitions-and-names"></a>

### 定义与名称

持久化任务定义是冷任务，即创建定义时不运行；对于 `async DurableTask` 方法，定义只能使用一次。每个已记录操作由其名称或它在未命名操作中的位置标识，因此应为每个调用位置使用独立名称，不包含 `$`，并在部署间保持稳定。[任务标识与引用](durable-tasks-identity.md)列出了运行时生成的各种标识符及影响其稳定性的变更。

<a id="scheduling-time-and-concurrency"></a>

### 调度、时间与并发

激活实例的调度器决定任务体操作的执行顺序，因此任务体应始终在该调度器上运行，并通过 `DurableTask.WhenAll`、`WhenAny`、`Race`、`ScheduleAll` 和 `WhenEach` 执行并发的持久化工作。这些组合器按索引标识输入。持久化延迟与超时使用逻辑时间记录其到期时间。[结构化并发](durable-tasks-structured-concurrency.md)描述组合器如何等待各输入进入终态。

<a id="cancellation-signals-logging-and-starts"></a>

### 取消、信号、日志记录与任务启动

持久化取消会被记录，宿主停止不会，因此处理程序应区分二者。信号为每个名称锁存一个值。重放代码中的日志会重复记录，除非日志记录器跳过重放。`Post` 在调用方 Grain 的发件箱中暂存启动操作，而客户端没有这样的发件箱。

<a id="rules-which-the-analyzers-dont-check"></a>

## 分析器未检查的规则

这些规则依赖分析器无法观察的数据或部署历史：

- **根据有序输入进行调度。** 使用数组、列表或排序后的序列构造组合器输入和调度持久化工作的循环。`HashSet<T>` 或删除过元素的字典可能在另一个进程中以不同顺序枚举，从而重新分配输入索引和出现次数编号。
- **根据重放期间稳定的配置作出决策。** 重放读取的配置（例如设置循环边界的选项）在相关执行实例尚未完成时必须保持不变。已记录的延迟在配置时长改变后仍使用已记录的到期时间。
- **不要根据 `IsReplaying` 进行分支。** `DurableTask.Current.IsReplaying` 用于日志记录和诊断。如果某次运行在工作提交前结束，它会被重放；因此，仅在 `IsReplaying` 为 `false` 时运行的代码可能执行两次，也可能一次都不执行。
- **通过新增操作或版本分支实现版本演进。** 在执行实例尚未完成时，保留已有的操作、名称及参数：重放调用依据方法和目标匹配记录，而不是参数，并返回原始参数对应的已记录结果。在已有操作之后以新名称添加新操作；对尚未完成的执行实例已执行过的操作使用 `DurableTask.GetVersion` 分支处理变更；将新工作流结构添加为新方法。参见[持久化任务的版本控制](durable-tasks-versioning.md)。
- **限制历史记录的大小。** 每个已记录操作都会保留在任务树中，直到任务树被删除。无限循环的工作流应结束每一代运行，并以分离任务启动下一代，例如 `subscription-{id}-{generation}`。

<a id="configure-the-analyzers"></a>

## 配置分析器

这些规则适用于引用抽象包的所有项目。可以在 `.editorconfig` 中设置规则的严重程度，例如令重放时重复记录日志的情况导致构建失败：

```ini
[*.cs]
dotnet_diagnostic.ORLEANS0215.severity = error
```

每个诊断页面都说明了何时适合抑制该诊断。
