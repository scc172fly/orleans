---
title: 实现长期运行的提醒器
description: 通过在后台继续执行，使提醒器触发的工作能够运行超过 Grain 调用超时的时长。
ms.date: 08/21/2026
ms.topic: how-to
---

<a id="implement-long-running-reminders"></a>

# 实现长期运行的提醒器

Orleans 以 Grain 调用的方式调用 <xref:Orleans.IRemindable.ReceiveReminder*> 来投递提醒器。如果该调用持续未完成的时间超过已配置的响应超时，提醒器投递可能超时。

对于长期运行的提醒器工作，使用回调启动一个协作式后台循环，并立即返回。循环继续在 Grain 调度器上运行，并在各个有界工作单元之间让出执行权。如果循环停止或激活实例被替换，后续提醒器触发会再次启动它。

当一个 Grain 标识拥有可重新启动、需要在多次提醒器触发之间持续执行的工作时，使用此方案。将每次迭代建模为有界异步操作，并持久化足够的进度，以便在激活实例或进程丢失后恢复。

> [!IMPORTANT]
> 提醒器定义是持久化的，而后台任务属于当前激活实例。集群变化期间，提醒器触发可能被遗漏或重复投递。使每个工作单元具备幂等性，并根据持久化业务状态进行核对及恢复。

<a id="prerequisites"></a>

## 前置条件

- 在每个 Silo 上配置[提醒器提供程序](../grains/reminders.md#configure-reminder-storage)。
- 选择不小于 <xref:Orleans.Hosting.ReminderOptions.MinimumReminderPeriod?displayProperty=nameWithType> 且短于该 Grain [激活实例回收时限](../host/configuration-guide/activation-collection.md)的提醒器周期。
- 设计工作循环，使每次迭代都支持取消、异步让出执行权，并记录持久化进度。

<a id="schedule-a-frequent-reminder"></a>

## 调度频繁触发的提醒器

提醒器触发计入激活实例活动。短于激活实例回收时限的提醒器周期，会在正常空闲回收机制下保持 Grain 活跃，使后台任务能够持续推进。如果激活实例因关闭、故障、迁移或显式停用而终止，后续触发会再次激活该 Grain 并启动新的工作任务。

以较短周期注册提醒器：

:::code language="csharp" source="../snippets/compiled/HowTo/LongRunningReminderSnippets.cs" id="schedule_long_running_reminder":::

此示例使用默认的最短提醒器周期，即一分钟，该周期短于默认的激活实例回收时限 15 分钟。如果应用为任一值配置了不同设置，应确保提醒器周期不小于受支持的最短周期，且短于回收时限。

提醒器周期控制激活实例活动频率以及停止的工作任务重新启动的速度。后台循环控制实际工作的处理频率。

<a id="implement-the-worker"></a>

## 实现工作任务

以下 Grain 注册提醒器，在每个激活实例中最多启动一个工作任务，观察工作任务的故障，并在停用期间停止该任务：

:::code language="csharp" source="../snippets/compiled/HowTo/LongRunningReminderSnippets.cs" id="long_running_reminder_grain":::

该实现依赖以下行为：

1. <xref:Orleans.IRemindable.ReceiveReminder*> 作为 Grain 请求执行。返回 <xref:System.Threading.Tasks.Task.CompletedTask?displayProperty=nameWithType> 会完成此次提醒器投递，无须等待后台循环。
1. <xref:System.Threading.Tasks.ConfigureAwaitOptions> 控制首次 await 如何恢复执行。`ForceYielding` 确保循环在 `ReceiveReminder` 返回后启动，而 `ContinueOnCapturedContext` 使其在 Grain 调度器上恢复，在那里可以安全地访问 Grain 状态。
1. `_backgroundTask` 将每个激活实例限制为一个工作任务。如果工作任务在故障后完成，下一次提醒器触发会启动新的工作任务。
1. <xref:Orleans.Grain.OnDeactivateAsync*> 取消工作任务，并在运行时停用截止时间内等待它结束。进程突然终止可能跳过此回调，因此持久化进度仍是恢复依据。

将 `ProcessNextBatch` 替换为一个有界应用工作单元。每次迭代都应等待 I/O 或以其他方式让出执行权，使队列中的其他工作能够运行。由于循环等待期间可能执行其他 Grain 执行轮次，因此应重新检查任何会影响下一项操作的 Grain 状态。

通过 Grain 引用调用一次 `Start`。<xref:Orleans.GrainReminderExtensions.RegisterOrUpdateReminder*> 创建持久化调度；再次调用 `Start` 会更新同一个命名提醒器。

<a id="handle-failures-and-recovery"></a>

## 处理故障及恢复

工作任务观察并记录意外异常，因为 `ReceiveReminder` 已将 <xref:System.Threading.Tasks.Task.CompletedTask?displayProperty=nameWithType> 返回给提醒器运行时。故障工作任务完成后，下一次提醒器触发可以将其重新启动。

推进到下一个工作单元之前，持久化检查点。典型迭代过程：

1. 从持久化状态读取下一个未完成的项。
1. 使用稳定的操作标识符执行幂等副作用。
1. 持久化完成记录。
1. 继续处理下一项。

这一顺序使新激活实例能够在回收、迁移、Silo 关闭或进程故障后核对并恢复中断的操作。

<a id="verify-the-recipe"></a>

## 验证方案

1. 调用 `Start`，确认首次提醒器触发启动一个工作任务。
1. 在该工作任务活跃时让另一次提醒器触发到达，确认仍然只有一个工作任务处于活跃状态。
1. 停用 Grain 或重启托管 Silo，确认后续提醒器触发会创建新激活实例，并从持久化检查点恢复。
1. 使一次工作迭代失败，确认异常已记录，并且下一次提醒器触发会重新启动工作任务。

对于只在 Grain 活跃时执行的周期回调，使用 [Grain 定时器](../grains/timers.md)。对于可在一次提醒器调用内完成的工作，直接从 `ReceiveReminder` 返回工作任务。
