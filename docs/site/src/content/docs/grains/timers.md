---
title: Grain 定时器
description: 以 Orleans Grain 执行轮次的方式调度激活实例范围内的周期性工作。
ms.date: 10/15/2026
ms.topic: concept-article
---

<a id="grain-timers"></a>

# Grain 定时器

Grain 定时器为一个 Grain 激活实例调度周期性工作。每次回调都作为该激活实例上的一个 Grain 执行轮次执行，并遵循 Orleans 的请求调度、跟踪和 Grain 调用筛选器行为。运行时在整个激活实例生存期内管理定时器，并在该实例停用时丢弃它。

对于生存期和状态都属于当前激活实例的频繁工作，请使用 Grain 定时器。如果调度计划属于逻辑 Grain，并且必须在激活实例或集群生命周期变化后继续存在，请使用 [提醒器](reminders.md)。

如需将回调作为一次 Grain 请求执行，请使用 <xref:Orleans.GrainBaseExtensions.RunRequestAsync*>。参阅 [请求调度](request-scheduling.md#run-background-work-as-a-grain-request)。

<a id="register-a-grain-timer"></a>

## 注册 Grain 定时器

使用 <xref:Orleans.GrainBaseExtensions.RegisterGrainTimer*> 注册定时器。<xref:Orleans.Grain.RegisterTimer*> 已过时。

:::code language="csharp" source="../snippets/compiled/Grains/WorkersAndTimersSnippets.cs" id="grain_timer":::

<xref:Orleans.GrainBaseExtensions.RegisterGrainTimer*> 返回 <xref:Orleans.Runtime.IGrainTimer>。如果激活实例需要修改或停止调度计划，请保留该句柄。

<a id="timer-behavior"></a>

## 定时器行为

<xref:Orleans.Runtime.GrainTimerCreationOptions> 控制初始调度计划和回调行为：

| 属性 | 默认值 | 运行时行为 |
|---|---:|---|
| <xref:Orleans.Runtime.GrainTimerCreationOptions.DueTime> | 必填 | 延迟首次回调。<xref:System.TimeSpan.Zero> 立即调度回调，<xref:System.Threading.Timeout.InfiniteTimeSpan> 使定时器保持暂停。 |
| <xref:Orleans.Runtime.GrainTimerCreationOptions.Period> | 必填 | 当前回调完成后，延迟下一次回调。<xref:System.Threading.Timeout.InfiniteTimeSpan> 只调度一次回调。 |
| <xref:Orleans.Runtime.GrainTimerCreationOptions.Interleave> | `false` | 遵循 Grain 的正常可重入规则。`true` 允许回调与其他 Grain 调用和定时器交错执行。 |
| <xref:Orleans.Runtime.GrainTimerCreationOptions.KeepAlive> | `false` | 控制每次回调是否延长激活实例的空闲生存期。 |

<a id="callback-scheduling"></a>

### 回调调度

同一个定时器的回调绝不会与自身重叠。Orleans 等待回调任务完成，再计算一个周期的等待时间，然后调度下一次回调。因此，回调本身的执行耗时也会计入相邻两次回调开始之间的间隔。

定时器回调是发送给其激活实例的本地消息，参与正常的执行轮次调度，始终在注册该定时器的激活实例上执行。

<a id="interleaving"></a>

### 交错执行

当 <xref:Orleans.Runtime.GrainTimerCreationOptions.Interleave> 设置为 `false` 时，回调像 Grain 方法调用一样遵循 Grain 的可重入配置。非可重入 Grain 执行回调时不会与其他请求交错；可重入 Grain 可以根据其调度规则与其他请求交错执行。

如果回调可以安全地处理其他执行轮次在各个 await 点之间导致的 Grain 状态变化，可以将 <xref:Orleans.Runtime.GrainTimerCreationOptions.Interleave> 设置为 `true`。

<a id="activation-lifetime"></a>

### 激活实例生存期

默认情况下，定时器回调不会改变激活实例的空闲生存期。Orleans 可以回收除此之外处于空闲状态的激活实例，这会终止该实例拥有的所有定时器。

当 <xref:Orleans.Runtime.GrainTimerCreationOptions.KeepAlive> 设置为 `true` 时，每次回调都会延长激活实例的生存期。若定时器周期短于配置的空闲回收周期，连续回调会使该激活实例保持活动。回调不够频繁时，仍可能在两次触发之间回收实例。

<a id="change-or-stop-a-timer"></a>

## 修改或停止定时器

调用 <xref:Orleans.Runtime.IGrainTimer.Change*> 替换首次触发等待时间和周期。新的等待时间用于调度下一次回调，新的周期在该次回调完成后生效。如果在正在执行的回调内部修改，则在当前回调完成后生效。

释放 <xref:Orleans.Runtime.IGrainTimer>，以取消其回调令牌并停止后续回调。当激活实例开始停用时，Orleans 也会取消该令牌并释放定时器。

<a id="handle-callback-failures"></a>

## 处理回调故障

Orleans 会记录定时器回调产生的异常，并在配置的周期之后调度下一次回调。允许异常传播之前，应保持应用状态一致。如果恢复必须跨越激活实例故障，请使用 Grain 状态或其他持久化存储。

回调的 <xref:System.Threading.CancellationToken> 表示定时器被释放或激活实例正在关闭。异步工作应响应此令牌，以便及时完成停用。

<a id="poco-grains"></a>

## POCO Grain

直接实现 <xref:Orleans.IGrainBase> 的 Grain 可以使用 <xref:Orleans.GrainBaseExtensions.RegisterGrainTimer*>。如果基础设施代码需要通过当前 <xref:Orleans.Runtime.IGrainContext> 执行更底层的注册，请注入 <xref:Orleans.Timers.ITimerRegistry>。

<a id="troubleshoot-grain-timers"></a>

## Grain 定时器故障排查

| 观察到的现象 | 运行时行为及处理方式 |
|---|---|
| 激活实例回收或 Silo 故障后，定时器终止。 | 定时器属于该激活实例。在激活初始化期间注册，或者使用 [提醒器](reminders.md) 来保存能够跨越激活实例变化的调度计划。 |
| 回调开始时间相对于实际时钟间隔越来越晚。 | Orleans 从回调完成之后开始计算周期。缩短回调耗时、调整周期，或者使用持久化应用状态对实际时钟调度建模。 |
| 回调等待期间执行了其他 Grain 调用。 | Grain 可重入，或者定时器启用了 <xref:Orleans.Runtime.GrainTimerCreationOptions.Interleave>。应在 await 点之间保护状态不变量，或者采用非交错调度。 |
| 空闲激活实例仍保持活动。 | <xref:Orleans.Runtime.GrainTimerCreationOptions.KeepAlive> 在每次回调时延长生存期。如果应按正常空闲超时进行回收，将它设置为 `false`。 |
| 回调异常反复出现。 | Orleans 记录异常并继续调度。让回调基于持久化状态收敛到正确结果，然后解决底层故障。 |
