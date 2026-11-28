---
title: 持久化任务的版本控制
description: 在执行实例尚未完成时修改持久化任务代码，通过 DurableTask.GetVersion 版本分支使各执行实例遵循已记录的版本，并安全淘汰旧版本。
ms.date: 10/16/2026
ms.topic: how-to
---

<a id="version-durable-tasks"></a>

# 持久化任务的版本控制

部署期间尚未完成的持久化任务会在新代码上恢复：下一次激活从头重新运行任务体，各已记录的操作返回已记录结果。`DurableTask.GetVersion` 允许同一任务体包含某项变更各个版本的代码，并为每个执行实例提供它应遵循的版本：在旧版代码上启动的执行实例保留原有操作，新执行实例则执行新操作。

本文建立在[重放与确定性](durable-tasks.md#replay-and-determinism)和[任务标识与引用](durable-tasks-identity.md)之上。仅在已有操作之后增加新操作，或增加新方法的变更不需要版本控制；参见[安全地对工作流进行版本演进](durable-tasks-operations.md#version-workflows-safely)。

<a id="branch-on-a-changes-version"></a>

## 根据变更版本进行分支

下面的工作流原先通过仓库发货。后续部署改为预约承运商，再之后的部署还会通知客户。每次部署都保留此前的分支，版本检查声明代码支持的版本：

:::code source="../snippets/compiled-durable/Grains/DurableTaskVersioningSnippets.cs" id="durable_tasks_versioning_branch" language="csharp":::

一个执行实例首次检查某项变更时决定版本，并在该执行实例之下将其记录为 `$version:{name}`。此后，该执行实例对该变更的每次检查及每次重放都会返回已记录版本：

| 执行实例 | 版本 |
| --- | --- |
| 首次到达检查点：新执行实例，或此前运行在此点之前就停止的执行实例 | `maxSupported` |
| 已在不包含此检查的代码上越过此点 | `0`，即变更前的代码 |
| 已记录版本 | 已记录的版本 |
| 某个步骤，其同一 Grain 上的外层执行实例已记录版本 | 外层执行实例的版本 |

重放通过执行历史识别已经越过检查点的执行实例：当重放到达检查点时，此前运行记录的操作中仍有尚未执行的操作位于该点之后。请将版本检查放在变更增加、移除或修改的第一个操作之前，使每个执行实例到达检查点时，都能按自身版本的预期处理操作。

- 在循环内或多个辅助方法中检查同一变更，每次都返回第一次决策。
- 记录随执行实例的下一次持久化写入提交，与 `DurableTask.RunInline` 效果的记录相同；如果重放在该写入之前结束，下一次仍会根据相同历史作出决策。
- 版本决策的作用域是被调用任务的执行树：对其他 Grain 的调用决定其自己的版本。
- 决策适用于记录它的执行实例及其下的步骤。如果步骤先于父执行实例检查变更，则该步骤记录自己的决策，不会约束父执行实例；父执行实例后续检查时根据自己的历史作出决策。
- 已请求取消的任务会在任务体首次观察到请求的点重放该请求，因此任务体沿此前运行的补偿路径执行。若仅补偿路径能到达某个检查，且此前运行没有记录该检查，则会发现此前运行记录的操作位于检查之后，并决定使用 `0`。当补偿依赖版本时，应在任务体根据取消情况分支之前检查变更，例如在任务体开始处。
- 与 `DurableTask.RunInline` 一样，版本检查在激活实例的调度器上运行。在持久化任务之外，它返回 `maxSupported`。
- 名称遵循操作命名规则：不能为空，不能包含 `$`。[ORLEANS0218](../diagnostics/orleans0218.md) 会报告名称及范围不是常量的情况。

<a id="evolve-a-change"></a>

### 演进一项变更

每次部署该变更的新版本时，提高 `maxSupported` 并增加一个分支：

1. 第一次部署使用 `GetVersion("carrier-booking", 0, 1)` 检查版本：版本 1 预约承运商，版本 0 仍使用仓库。
2. 下一次部署使用 `GetVersion("carrier-booking", 0, 2)` 检查版本：版本 2 增加通知，已记录版本 1 的执行实例继续预约承运商而不发送通知。

任务体其他位置的无关变更应使用独立名称。为各版本的新操作分配新名称，避免其记录与另一版本的记录相遇。

<a id="declare-a-change-once"></a>

### 将变更集中声明一次

`DurableVersion` 在一个声明中包含变更的名称和支持范围，类似于 `DurableSignal<T>` 包含信号名称。此后对该变更的每次检查都声明相同范围，部署增加或淘汰版本时只需修改该声明：

:::code source="../snippets/compiled-durable/Grains/DurableTaskVersioningSnippets.cs" id="durable_tasks_versioning_descriptor" language="csharp":::

`Get()` 返回执行实例的版本，`IsAtLeast(version)` 报告执行实例是否遵循 `version` 或更新版本。

<a id="retire-a-version"></a>

## 淘汰版本

当不再有尚未完成的执行实例遵循某个版本时，就可以淘汰它：

1. 查找遵循该版本的执行实例。每个执行实例将其决策保存在 `$version:{name}` 记录中，其响应是版本；`VersionRecorded` 事件在记录决策时报告它。参见[监控版本](#monitor-versions)。
2. 将 `minSupported` 提高到该版本之上，并删除其分支：

   :::code source="../snippets/compiled-durable/Grains/DurableTaskVersioningSnippets.cs" id="durable_tasks_versioning_retired" language="csharp":::

3. 一旦 `minSupported` 等于 `maxSupported`，且所有尚未完成的执行实例都遵循该版本，即可移除检查。执行实例持有的记录会继续保留在任务树中，直至任务树删除；重放会跳过这些记录。

后续变更应使用新名称，而不要复用已淘汰的名称：如果某个执行实例在该已淘汰的检查尚未加入时就越过了检查点，它没有相关记录；用旧名称进行新检查会为它决定版本 `0`。

<a id="stalled-executions"></a>

## 停滞的执行实例

如果正在运行的代码无法重放某个执行实例，该执行实例将停滞。运行时使用以下两种原因之一取消本次执行尝试，方式与取消正在停止的激活实例的执行尝试相同。参见[任务取消与执行尝试取消](durable-tasks-structured-concurrency.md#task-cancellation-and-attempt-cancellation)：

| 原因 | 触发条件 | 内部异常 |
| --- | --- | --- |
| `UnsupportedVersion` | 执行实例已记录的版本超出当前运行代码所支持的范围。 | `DurableTaskVersionException`，包含变更名称、已记录版本及支持范围 |
| `Diverged` | 重放在已记录 ID 下请求了另一个操作；已记录结果无法再解码；任务存储的请求无法再解码；或任务体在此前运行首次观察到任务取消的点之前就已结束。参见[诊断重放分歧](durable-tasks-operations.md#diagnose-replay-divergence)。 | `InvalidOperationException`，指出已记录及请求的操作、解码失败或未到达的取消点 |

运行时的行为：

- 从发现停滞的操作抛出 `DurableTaskAttemptCanceledException`，附带上表中的内部异常。本次尝试的后续持久化操作抛出同样的取消异常，且 `AttemptCancellationToken` 被取消，因此本次尝试通过 `finally` 块完成堆栈展开。
- 无论任务体如何处理异常，均丢弃本次尝试的结果。
- 任务保持待完成状态。等待该任务的调用方继续等待。
- 记录警告并触发 `ExecutionStalled` 事件，携带 `Reason` 和 `Exception`。
- 按 `DurableTaskOptions.StalledExecutionRetryInterval`（默认一分钟）为 Grain 保留周期性唤醒，使之后的激活实例能够重放任务，例如在下次部署的 Silo 上重放。

当部署提高 `minSupported`，而仍有执行实例遵循已淘汰的版本时，或者支持较少版本的代码重放某执行实例时（例如回滚期间），就会发生版本停滞。部署支持已记录版本的代码，或与发生分歧的执行实例记录相匹配的代码，可使执行实例恢复；也可以中止任务，参见[中止卡住的任务](durable-tasks-operations.md#abort-stuck-tasks)。

<a id="rolling-deployments"></a>

### 滚动部署

滚动部署期间，Grain 可能在仍运行旧代码的 Silo 上激活。旧代码会使已记录版本高于其 `maxSupported` 的执行实例停滞，在运行新代码的 Silo 激活该 Grain 之前，它们都不会恢复。某项变更首次增加版本检查之前的代码不包含该检查，因此会让所有执行实例继续使用变更前的代码，包括由更新代码启动的执行实例。

应先排空旧执行实例。增加某项变更的首次版本检查或提高 `maxSupported` 的部署，必须在所有 Silo 上完成后，工作才能依赖新版本；由旧代码启动的执行实例也必须完成，后续部署才能淘汰其版本。

<a id="monitor-versions"></a>

## 监控版本

`DurableTaskEvents` 侦听器报告每次已记录决策及每次停滞。统计决策的监控器可以显示执行实例仍在记录哪些版本，并记录停滞事件：

:::code source="../snippets/compiled-durable/Grains/DurableTaskVersioningSnippets.cs" id="durable_tasks_versioning_monitor" language="csharp":::

`VersionRecorded` 报告执行实例、变更名称和版本：对于旧代码已经越过检查点的执行实例，版本为 `0`。`ExecutionStalled` 报告停滞的执行实例、`Reason`（`UnsupportedVersion` 或 `Diverged`）及描述停滞的 `Exception`。

<a id="choose-versions-or-new-methods"></a>

## 选择版本控制还是新增方法

- **在原任务体中进行版本控制：** 执行实例尚未完成时，如果在任务体内部修改，例如替换步骤、改变参数或新增通知，则采用此方式。
- **新增方法：** 如果需要新的工作流结构，则用新方法调度新工作，并保留旧方法，直到它尚未完成的任务全部完成。
- **启动新一代运行：** 对于无限运行的工作流，每一代结束时将下一代作为分离任务启动，例如 `subscription-{id}-{generation}`；新一代使用启动当时的最新代码运行。
