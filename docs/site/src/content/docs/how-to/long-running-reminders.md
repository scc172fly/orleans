---
title: "执行长期催复通知"
description: "运行提醒触发的工作, 其时间超过 grain 调用超时, 并在背景中继续 。"
ms.date: 08/21/2026
ms.topic: how-to
---

<a id="implement-long-running-reminders"></a>

# 执行长期催复通知

Orleans 发送提醒, 以调用 <xref:Orleans.IRemindable.ReceiveReminder*> 的方式作为 grain 的呼叫。 保持该响应不全的通话时间超过配置的反应超时可以导致发送提醒的时间过长 。

对于长期提醒工作, 请使用回调来启动一个合作背景循环并迅速返回。 循环继续运行在 grain 调度器上, 并在有约束的工作单位之间产生效果。 如果循环停止或激活被替换, 稍后的提醒勾起再次开始 。

当一个 grain 身份拥有可恢复的工作, 应在提醒勾勾之间继续。 将每次迭代都作为捆绑的、 无同步操作, 并持续足够进展, 在激活后或处理丢失后可以恢复 。

> [!IMPORTANT]
> 提醒的定义是耐久的, 而背景任务属于当前激活的任务。 在群集更改时, 提醒勾点可以错失或不止一次执行。 让每个工作单位都具备最佳能力, 并与持久的商业状态进行调和 。

<a id="prerequisites"></a>

## 先决条件

- 配置 a [提醒提醒提供者](../grains/reminders.md#configure-reminder-storage) 上每 silo.
- 在 <xref:Orleans.Hosting.ReminderOptions.MinimumReminderPeriod?displayProperty=nameWithType> 或以上选择提醒期,且时间短于 grain的 [激活收集年龄](../host/configuration-guide/activation-collection.md)。
- 设计工作循环,使每个迭代都接受取消、不同步地产生结果并记录持久进展。

<a id="schedule-a-frequent-reminder"></a>

## 经常安排提醒通知

提醒计数为激活活动。提醒时间短于启动收集年龄,提醒时间使 grain 在正常闲置收藏中有效,允许背景任务继续取得进展。如果由于关闭、故障、迁移或明确停止功能而终止激活,稍后的答题再次激活 grain 并开始新工人。

登记短时期的提醒:

:::code language="csharp" source="../snippets/compiled/HowTo/LongRunningReminderSnippets.cs" id="schedule_long_running_reminder":::

此示例使用默认最小提醒时间为1分钟, 短于15分钟的默认激活收集年龄。 如果应用程序配置不同值, 请将提醒时间保持在支持的最低和低于采集年龄的最小 。

提醒期控制激活活动,停止的工人恢复工作的速度有多快。背景循环控制了工作的处理频率。

<a id="implement-the-worker"></a>

## 落实工人

以下 grain 登记提醒,每次启动最多开始一名工人,观察工人的失败情况,并在停止工作时停止工作:

:::code language="csharp" source="../snippets/compiled/HowTo/LongRunningReminderSnippets.cs" id="long_running_reminder_grain":::

执行取决于以下行为:

1. <xref:Orleans.IRemindable.ReceiveReminder*> 执行请求 grain 。返回 <xref:System.Threading.Tasks.Task.CompletedTask?displayProperty=nameWithType> 完成提醒发送,无需等待背景循环。
1. <xref:System.Threading.Tasks.ConfigureAwaitOptions> controls how the first await resumes. `ForceYielding` ensures that the loop starts after `ReceiveReminder` returns, and `ContinueOnCapturedContext` resumes it on the grain scheduler, where it can safely access grain state.
1. `_backgroundTask` 限制一个工人的激活。 如果该工人在失败后完成, 下次提醒会选中开始一个新的工人 。
1. <xref:Orleans.Grain.OnDeactivateAsync*> 取消工人的取消,并在运行期的终止期限内等待。 程序中断可能跳过此回召, 因此持久进展仍然是恢复的来源 。

将 `ProcessNextBatch` 替换为一个约束的应用程序工作单位。 每次迭代应等待 I/O 或以其他方式产生,以便其它队列的工作能够运行。由于其他 grain 转弯可以在循环等待时执行,因此重新检查其值影响下一个操作的任何 grain 状态。

调调 `Start` 一次通过 a grain 参考。 <xref:Orleans.GrainReminderExtensions.RegisterOrUpdateReminder*> 创建耐久时间表;调用 `Start` 再次更新同一命名的提醒通知。

<a id="handle-failures-and-recovery"></a>

## 处理失败和复原

工人观察和记录意外例外,因为 `ReceiveReminder` 返回 <xref:System.Threading.Tasks.Task.CompletedTask?displayProperty=nameWithType> 提醒运行时间。 完成失败的工人, 让下次提醒时再重新激活它 。

向下一级单位推进前,先维持一个检查站。

1. 从耐久状态读取下一个不完整的项目 。
1. 使用稳定的操作识别符,执行一无所能的副作用。
1. 记录完成时间可久。
1. 继续下一个项目。

此序列可以让新激活调节采集、迁移、 silo 关闭或进程失败后中断的操作。

<a id="verify-the-recipe"></a>

## 校验食谱

1. 拨打 `Start` 并确认第一个提醒键开始一个工人。
1. 让另一个提醒在工人积极活动时滴答到,并证实一名工人仍然活跃。
1. 关闭主机或重新启动主机 silo ,并确认后一个提醒滴答会产生新的启动并恢复从耐用检查站的运行。
1. 使一个重复工作失败,确认例外被登录,下次提醒勾将重新启动工人。

只有在 grain 活动期间才运行的定期回调,使用 [grain 计时器](../grains/timers.md)。对于在一次提醒中完成的工作,请直接从 `ReceiveReminder`返回工作任务。
