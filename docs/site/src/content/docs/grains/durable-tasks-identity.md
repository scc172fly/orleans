---
title: 持久化任务的标识与引用
description: 介绍持久化任务 ID 的构造方式、调用方、句柄和诊断所提供的 ID，以及如何保持 ID 在重放和部署之间稳定、存储并传递任务引用。
ms.date: 10/15/2026
ms.topic: reference
---

<a id="durable-task-identity-and-references"></a>

# 持久化任务的标识与引用

每个持久化任务都由托管它的 Grain 和一个分层的 <xref:Orleans.Durable.Tasks.TaskId> 标识。重放依靠标识查找每个已记录的操作；重试启动请求时，标识确保请求到达同一任务；被调用方依靠标识区分调用方；其他参与方也依靠标识观察、取消任务及向任务发送信号。本文列出运行时构造的各种 ID、可以观察到这些 ID 的位置、哪些变更能保持 ID 不变，以及如何从其他位置寻址任务。

<a id="the-shape-of-a-task-id"></a>

## 任务 ID 的结构

任务 ID 是由 `/` 分隔的路径：首先是根段，每一层嵌套再增加一个路径段。`DurableTask.Current.Id` 返回正在运行的执行实例的 ID，`TaskId.Child(segment)`、`Parent()` 和 `IsAncestorOf(other)` 可用于在路径中导航。

- `TaskId.ToString()` 返回转义后的路径，`TaskId.Parse` 可以将其解析回来。路径段内部的 `/` 或 `\` 使用 `\` 转义，因此包含 Grain ID（例如 `report/q3`）的路径段显示为 `report\/q3`。
- `TaskId.CreateRoot(segment)` 及接受 `string rootId` 的重载（例如 `GetDurableTask<TResult>(rootId)` 和 `IDurableTaskManager.CancelAsync(rootId, reason)`）都接受一个未转义的根段。
- 运行时生成的路径段以 `$` 开头或包含该字符。在持久化任务内部，传给 `WithId` 和 `DurableTask.RunInline` 的名称不能包含 `$`，因此名称绝不会与生成的路径段相同；参见 [ORLEANS0207](../diagnostics/orleans0207.md)。

下面的工作流会创建大多数类型的 ID。注释指出每个操作记录的 ID：

:::code source="../snippets/compiled-durable/Grains/DurableTaskIdentitySnippets.cs" id="durable_tasks_identity_tree" language="csharp":::

<a id="kinds-of-ids"></a>

## ID 的类型

<a id="root-ids"></a>

### 根 ID

根任务在所属 Grain 上没有父任务。启动该任务的一方决定其根 ID：

| 启动方 | 根 ID | 示例 |
| --- | --- | --- |
| 客户端或普通 Grain 方法，使用 `WithId(name)` | 原样使用 `name` | `publish-q3` |
| 客户端或普通 Grain 方法，不使用 `WithId` | 按 `"N"` 格式生成的新 GUID | `3f2a9c01b7e44d6b9a0c5e8f1d2b3a47` |
| 持久化任务，使用 `.Detached(name)` | 原样使用 `name` | `archive-q3` |
| 持久化任务，使用 `.Detached()` 且未指定名称 | 单个路径段 `{grainId}/{taskId}/{segment}`：启动方 Grain、启动方执行实例的 ID，以及该执行实例下一个未命名操作的生成路径段；分别转义各部分中的 `/` 和 `\` | `report\/q3/publish-q3/$child-2` |

根 ID `$caller` 保留给下文介绍的调用方作用域：只有来自其他 Grain 的调用才能在其下创建任务，客户端或 Grain 尝试以它作为启动任务的根 ID 时将抛出 `ArgumentException`。在持久化任务之外，`.Detached()` 不会改变任何行为，因为客户端启动的每个任务本来就是根任务。派生的分离任务根 ID 包含启动方 Grain，因此不同 Grain 中具有相同任务 ID 的工作流会启动不同的任务；若多个启动方必须共享同一任务，应使用 `WithId` 为其命名。

<a id="operations-of-an-execution"></a>

### 执行实例的操作

任务体执行的每个操作都记录在执行实例 ID 之下：

| 操作 | 路径段 | 示例 |
| --- | --- | --- |
| 通过 `WithId(name)` 命名的子任务、延迟、组合器或订阅，或者已记录效果 `DurableTask.RunInline(name, effect)` | `name`；一次运行中再次出现同一名称时，依次为 `name$2`、`name$3` 等 | `publish-q3/check$2` |
| 未命名的子任务、延迟、`DurableTask.Run` 步骤、订阅或 `DurableTask.RunInline(effect)` | `$child-{n}`，按本次运行创建它们的顺序编号 | `publish-q3/$child-1` |
| 未命名的组合器或超时操作 | `${kind}-{n}`，其中 `{kind}` 为 `when-all`、`when-any`、`race`、`schedule-all`、`when-all-completed`、`when-each` 或 `timeout` | `publish-q3/$race-1` |
| 组合器的输入 | `{combinator}/{index}`，按输入顺序排列 | `publish-q3/render/0` |
| 超时操作中的实际工作和延迟，即 `WaitAsync` 和 `WaitUntilAsync` | `{timeout}/0` 和 `{timeout}/1` | `quote/0`、`quote/1` |
| 无法通过持久化完成顺序确定的选择 | `WhenAny`、`Race` 和超时使用 `{combinator}/$winner`，`WhenEach` 使用 `{combinator}/$next-{n}` | `first-quote/$winner` |
| 版本决策 `DurableTask.GetVersion(name, ...)` | `$version:name`，每个执行实例记录一次 | `publish-q3/$version:carrier-booking` |
| 分离启动 | `$detach:{targetGrainId}:{rootId}` | `publish-q3/$detach:archive\/archive:archive-q3` |
| 从持久化任务发送信号 | `$send:{targetGrainId}:{taskId}:{name}`；重复发送追加 `$2`、`$3` | `place-order-42/$send:order\/42:place-order-42:shipped` |
| 信号记录的值，位于被调用任务之下 | `$signal:{name}`；`DurableSignal<T>.For(suffix)` 使用名称 `{name}/{suffix}` | `publish-q3/$signal:approved`、`publish-q3/$signal:approved\/round-2` |

- 命名子任务、命名组合器和命名效果共用同一名称的出现次数所对应的标识符：`DurableTask.RunInline("x", f)` 与 `await DurableTask.Run(g).WithId("x")` 都记录在 `x` 之下。如果重放时发现该标识符下记录的是另一类操作，就会发生重放分歧，而不是再次执行该工作；执行实例将停滞，分歧信息会指出这两种操作类型。
- 未命名的子任务、效果、订阅及未命名分离根任务的路径段共用 `$child-{n}` 计数。未命名的组合器和超时共用 `${kind}-{n}` 计数。
- 在 `$detach:` 和 `$send:` 路径段各组成部分内部，`:`、`\` 和 `$` 使用 `\` 转义。
- 仅当完成顺序不能决定选择结果时（例如某个输入完成时没有完成序号），才记录选择。当选择操作进入等待时，其 ID 出现在 `SelectionWaitStarted` 诊断事件中。
- 每个本地步骤都有自己的执行实例，其操作嵌套在该执行实例的 ID 下，例如 `publish-q3/check/$child-1`。

<a id="calls-to-other-grains"></a>

### 调用其他 Grain

对其他 Grain 的调用记录在调用方的任务树中，位于该调用的路径段下，例如 `publish-q3/render/0`。Grain 之间的消息使用调用的*传输 ID*（wire ID）：由调用方本次运行的运行世代标识（incarnation）及调用的各路径段组成。运行世代标识是调用方 Grain 在存储根任务调用时生成的 GUID，采用 `"N"` 格式。被调用方按传输 ID 将任务存储在调用方作用域下：

```text
{incarnation}/{callerTaskId}
$caller/{callerGrainId}/{incarnation}/{callerTaskId}
$caller/report\/q3/9b1c0e4f2d7a4e8c8f3b5a6d1e2c3b4a/publish-q3/render/0
```

作用域将调用方彼此隔离：两个 Grain 的工作流即使具有相同的任务 ID，也会使用不同的 ID 调用同一被调用方，因此不会重新附接到对方的任务。运行世代标识区分不同次运行：Grain 删除根任务树之后，再次调度相同根 ID 会使用新的运行世代标识运行该任务，使其调用到达新的任务，消息不会与先前运行的消息去重；先前运行的迟到回复（例如完成通知或延迟恢复通知）也不会改变任何内容。同一次运行内，该映射是调用方、运行世代标识和子任务 ID 的纯函数，因此重放和重新投递同一调用仍到达同一任务。被调用方的 `DurableTask.Current.Id` 返回带作用域的 ID，`DurableTask.Current.Incarnation` 返回各任务树的运行世代标识。分离启动则发送根 ID，由被调用方原样存储。订阅与分离启动也使用传输 ID 标识用于记录的子任务，作为结果完成通知寻址的订阅者。

<a id="signals-and-subscriptions"></a>

### 信号与订阅

信号寻址的是一个*被调用的任务*：根任务或由其他 Grain 调用的任务。其值以 `$signal:{name}` 的形式记录在该任务之下，任务树中任意位置的等待操作都能找到它。未命名等待作为等待方执行实例的一部分运行，不单独记录操作；由 `WithId` 命名的等待则作为该名称下的子任务运行。对等待使用取消屏蔽不会改变其标识。

在持久化任务内等待 `DurableTaskReference<TResult>`、调度该引用或将其传给组合器，都会在该操作分配的 ID 下记录一个订阅子任务，例如 `$child-3` 或通过 `WithId` 指定的名称。该子任务以所引用任务的结果完成。

<a id="where-each-id-is-visible"></a>

### 在何处获取各类 ID

| 来源 | 返回的内容 |
| --- | --- |
| `DurableTask.Current.Id` | 当前运行的执行实例：根任务、步骤或带调用方作用域的被调用任务。 |
| `ScheduledTask.Id` | 启动或观察任务的执行实例中的操作 ID：子任务 ID、`$detach:` 记录或订阅子任务。在持久化任务之外则为根 ID。 |
| `ScheduledTask.TargetTaskId` | 存储任务的 Grain 上的任务 ID，供其他参与方寻址：分离任务的根 ID、被调用方的 `$caller/...` ID 或订阅观察的任务。在持久化任务之外则为根 ID。 |
| `IDurableTaskReference.Id` | 所引用的任务；`Target` 表示其所属 Grain。 |
| `DurableTaskAddress.Id` | 供其他参与方寻址的任务；`Target` 表示其所属 Grain。 |
| `DurableTaskEvents` | 每次状态转换涉及的任务、操作、信号等待和选择的 ID。 |

客户端可通过句柄获取自己所启动任务的各类 ID：

:::code source="../snippets/compiled-durable/Grains/DurableTaskIdentitySnippets.cs" id="durable_tasks_identity_client" language="csharp":::

<a id="internal-names"></a>

### 内部名称

运行时还使用一些不是 ID、也不属于编程模型的名称：日志化状态 `orleans.durable-tasks.runtime` 及其中的表（例如 `$tasks`、`$task-requests` 和 `$task-results`），以及以 `$` 开头的请求指纹（例如 `$effect`、`$version`、`$subscribe:`、`$signal-send:` 和 `$completion-decision:`）。诊断中会显示其中一部分；其格式可能随版本变化。

<a id="stability-across-replays-and-deployments"></a>

## 重放与部署之间的标识稳定性

重放使用新一轮运行计算出的 ID 查找每个已记录操作，因此只要每次运行都基于相同的输入计算 ID，它就是稳定的：

| ID | 保持稳定的条件 |
| --- | --- |
| 通过 `WithId` 命名的根 ID | 始终稳定：调用方自行派生。 |
| 命名操作 `name` 或 `name$n` | 名称不变，且此前同名操作的出现情况不变。 |
| `$child-{n}` 和 `${kind}-{n}` | 每次运行都以相同顺序创建位于它之前的未命名操作。 |
| 组合器输入 `{combinator}/{index}` | 组合器的 ID 和输入顺序不变。 |
| `$detach:`、`$send:` 和 `$signal:` 记录 | 始终稳定：从目标、任务和名称派生。 |
| `$version:` 记录 | 始终稳定：从变更名称派生；同一执行实例中对该名称的每次检查共用一条记录。 |
| 派生的分离任务根 ID | 启动方执行实例的 ID 及启动前的 `$child-{n}` 计数不变。 |
| 调用方作用域内的 ID | 调用方的子任务 ID 不变。 |

ID 定位记录；记录中的操作决定重放是否与记录匹配。如果重放请求同一类型、同一目标的操作，就会匹配：调用同一 Grain 上的同一接口方法、使用同一根 ID 分离启动同一方法、订阅同一任务，或者执行延迟、步骤、效果。运行时通过生成的可调用类型识别方法，而不比较参数。因此，部署时修改参数的序列化形式（例如添加可选字段或升级序列化器）仍可重放同一操作，而重放时传入不同参数则会读取原始调用记录的结果。使用同一 ID 重试根任务也以相同方式匹配：同一调用方调用同一方法，无论参数为何，均到达已存储的任务；不同方法或调用方则发生冲突。每条记录还存储请求序列化参数的指纹，分歧和冲突消息会报告该指纹。

部署间的变更对尚未完成的执行实例有如下影响：

- **更改 `WithId` 指定的名称**会为操作分配新的 ID。重放在新 ID 下找不到记录，因此会再次执行操作，旧名称下的记录则保留在任务树中，直到任务树被删除。
- **在已有未命名操作前添加未命名操作**会使后续生成的 ID 偏移。重放在偏移后的 ID 下找到另一操作的记录：调用其他方法或 Grain 会发生分歧并使执行实例停滞；调用同一 Grain 上的同一方法则返回另一调用的结果；本地步骤会返回另一步骤的结果。
- **修改参数的序列化形式**或调用传递的参数，不会改变任何操作的标识。
- **添加具有新名称的命名操作**不会改变其他 ID。
- **在已有同名操作前添加或移除一次同名操作**会使后续出现次数重新编号；参见 [ORLEANS0208](../diagnostics/orleans0208.md)。

为以后可能移动的每个操作命名，给新操作分配新名称，并遵循[安全地对工作流进行版本演进](durable-tasks-operations.md#version-workflows-safely)中的说明。若要修改未完成执行实例已经经过的操作，请通过 `DurableTask.GetVersion` 进行版本分支；参见[持久化任务的版本控制](durable-tasks-versioning.md)。

<a id="address-a-task-from-elsewhere"></a>

## 从其他位置寻址任务

任务通过所属 Grain 和任务 ID 寻址。只要 Grain 仍保留该任务，知道这两项信息的参与方就可以观察、取消、中止任务或向任务发送信号。

<a id="durable-references-belong-to-root-tasks"></a>

### 持久化引用属于根任务

客户端、普通 Grain 方法和分离启动创建的根任务拥有独立的生命周期：任务完成后，所属 Grain 会将其保留 `ResultRetentionPeriod`，并继续保留直到向每个订阅者投递完成通知。这些任务适合建立持久化引用。

附接的子任务（包括被调用方的 `$caller/...` 任务）属于其所有者的任务树：调用方 Grain 接收结果后，被调用方会删除该任务；根任务进入终态后，所有者所在 Grain 会删除所有者的任务树。应在启动它的执行实例内使用其句柄，需要其他参与方跟踪的工作则应作为根任务启动。

<a id="derive-references-from-business-identity"></a>

### 根据业务标识派生引用

任务所属 Grain 和根 ID 由业务值派生时，各参与方可以直接计算引用，无需存储。上面启动的归档任务仅凭报告 ID 就能寻址：

:::code source="../snippets/compiled-durable/Grains/DurableTaskIdentitySnippets.cs" id="durable_tasks_identity_lookup" language="csharp":::

派生引用不需要存储，重试请求以同一名称启动时也会到达同一任务。请选择能标识业务操作的名称，例如 `archive-{reportId}`，并在部署间保持名称稳定。

<a id="store-and-send-addresses"></a>

### 存储和传递任务地址

<xref:Orleans.Durable.Tasks.DurableTaskAddress> 是在其他位置标识任务的值，包括其 `Target` Grain 和任务 `Id`。Orleans 会在 Grain 调用和日志化状态中序列化它；System.Text.Json 在 JSON 日志和 HTTP 有效载荷中将其写成 `{"Target":"report/q3","Id":"publish-q3"}`。单独的 `TaskId` 会序列化为转义后的路径。

| 目的 | 使用方式 |
| --- | --- |
| 读取句柄的地址 | `handle.TryGetAddress(out var address)`；对于根任务、分离任务或 `GetDurableTask` 观察的任务返回 `true`，其 `Id` 是句柄的 `TargetTaskId`。附接子任务的句柄没有地址。 |
| 读取引用的地址 | `DurableTaskReference<TResult>.Address`，或通过 `IDurableTaskReference` 获取 `Target` 和 `Id`。 |
| 解析地址 | Grain 内使用 `GrainFactory.GetDurableTask<TResult>(address)`，客户端内使用 `client.GetDurableTask<TResult>(address)`。 |
| 向任务发送信号 | `GrainFactory.GetGrain(address.Target)`，然后使用 `SignalDurableTaskAsync` 或 `TrySignalDurableTaskAsync` 并传入 `address.Id`。 |

通过地址解析出的引用与其他引用行为相同：持久化任务可以等待它以订阅任务结果，其他代码可通过它等待、轮询、取消或中止任务。移交分离任务的 Grain 存储任务地址，无论重新激活多少次，都可再次访问该任务：

:::code source="../snippets/compiled-durable/Grains/DurableTaskIdentitySnippets.cs" id="durable_tasks_identity_store" language="csharp":::

工作流在同步尾部保存地址，使地址与任务结果一同提交。`AwaitArchiveAsync` 在已记录效果中读取存储的地址，因此重放时会订阅同一任务。

客户端将地址返回给 HTTP 调用方；后续请求可以将地址传回以发送信号、等待或取消任务：

:::code source="../snippets/compiled-durable/Grains/DurableTaskIdentitySnippets.cs" id="durable_tasks_identity_send" language="csharp":::

持有地址就能控制任务：任何能够调用 Grain 的参与方都可以取消、中止任务或发送信号。对携带地址的请求执行操作前，必须像对待外部请求触发的其他 Grain 调用一样进行授权。

<a id="retention-and-not-found-results"></a>

### 保留期限与未找到任务的结果

只要 Grain 保留任务，引用就可以解析到该任务：

- 没有调用方 Grain 的任务（例如客户端启动或分离启动的根任务），完成后会保留 `ResultRetentionPeriod`，并保留至向每个订阅的持久化任务投递完成通知。
- 由其他 Grain 调用的任务，在调用方 Grain 持久化接收其结果后删除。

删除后，轮询和订阅会抛出 `DurableTaskNotFoundException`，信号操作返回 `TaskNotFound`，使用相同 ID 再次启动会作为新的运行执行请求，并拥有自己的运行世代标识。即使仍处于 Messaging 的 `DeduplicationWindow` 内也是如此：新运行的调用、取消、订阅、信号及延迟恢复都有自己的消息 ID，先前运行的迟到回复无法到达新运行。`DurableTaskNotFoundException` 也表示任务从未启动，或者启动请求尚未到达其所属 Grain；`ScheduleAsync` 在 Grain 持有任务后才返回，因此它返回的 ID 在任务保留期结束前都可以找到。参见[保留与删除](durable-tasks-operations.md#retention-and-deletion)。
