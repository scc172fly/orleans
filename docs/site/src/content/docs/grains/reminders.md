---
title: "提醒提醒"
description: "能够维持激活和集群生命周期变化的可持久定期 grain 工作。"
ms.date: 08/21/2026
ms.topic: concept-article
---

<a id="reminders"></a>

# 提醒提醒

提醒会为一个逻辑 grain保存定期时间表。配置的提醒提供者保留了定义,而 Orleans 则通过正常 grain 请求列表传送滴答。当没有当前激活时, 键会启动 grain 。

提醒必须用于工作, 其时间表必须在激活变化和集束重新启动后有效。 提醒时用分钟、 小时或天计的时长。 提醒可以唤醒一个 grain , 然后为精细放大的激活范围的工作创建 [grain 计时器](timers.md) 。

<a id="receive-reminder-ticks"></a>

## 接收提醒滴答

A grain 收到催复通知 <xref:Orleans.IRemindable>:

:::code language="csharp" source="../snippets/compiled/Grains/WorkersAndTimersSnippets.cs" id="remindable_report_grain":::

Orleans 援引 <xref:Orleans.IRemindable.ReceiveReminder*> ,并注明提醒的名称和 <xref:Orleans.Runtime.TickStatus>。回调执行为 grain 请求,并遵循 grain的时间安排和重新使用规则。

当提醒工作超过 grain 调用超时,请使用 [长期的催提醒食谱](../how-to/long-running-reminders.md) 启动可重新启用的工人并迅速返回。

<a id="register-or-update-a-reminder"></a>

## 登记或更新催复通知

a 记名的 grain催复通知:

:::code language="csharp" source="../snippets/compiled/Grains/WorkersAndTimersSnippets.cs" id="register_reminder":::

<xref:Orleans.GrainReminderExtensions.RegisterOrUpdateReminder*> 创建了耐久定义。 注册同一名称将再次取代其到期时间和期限 。

返回的 <xref:Orleans.Runtime.IGrainReminder> 是当前注册的一个控点。 在应用程序中保留提醒名称, 并在需要时在激活后获取当前控点 。

<a id="retrieve-or-remove-a-reminder"></a>

## 获取或删除提醒

获取当前控点并解除提醒登记 :

:::code language="csharp" source="../snippets/compiled/Grains/WorkersAndTimersSnippets.cs" id="unregister_reminder":::

使用 <xref:Orleans.GrainReminderExtensions.GetReminder*> 来提醒一个被命名的提醒,使用 <xref:Orleans.GrainReminderExtensions.GetReminders*> 来提醒 grain登记的所有提醒。

<a id="reminder-behavior"></a>

## 提醒行为

提醒提供者长期保存着每个定义。负责者 silo 装入定义,从持续启动时间和期间计算事件,并向 grain发出滴答请求。

个人滴答信息是短暂的。 集群无法获取或所有权移动可能导致预定事件得不到落实,而持久定义则继续产生以后的事件。 所有权趋同还可能产生重复回溯执行。 每一次回调都会自动进行,并协调来自持久商业状态的工作。

<xref:Orleans.Runtime.TickStatus.FirstTickTime> and <xref:Orleans.Runtime.TickStatus.Period> identify the theoretical schedule. <xref:Orleans.Runtime.TickStatus.CurrentTickTime> records when the runtime initiated the current delivery. Applications can compare those values with persisted progress to detect and reconcile missed occurrences.

Orleans 向一个启动 grain 发送激活 grain 必要时,包括收集先前的启动活动或 silo 停止了。

<a id="reminder-timing-constraints"></a>

## 提醒时间限制

提醒登记接受这些边界的时间表:

- `dueTime` 是 <xref:System.TimeSpan.Zero> 或更大。 零计划立即排第一个选中 。
- `dueTime` 符合登记时间所剩 <xref:System.DateTime> 的范围。
- `period` 是正值,至少 <xref:Orleans.Hosting.ReminderOptions.MinimumReminderPeriod?displayProperty=nameWithType>。
- 默认最低期限为1分钟。

运行时间拒绝消极价值, <xref:System.Threading.Timeout.InfiniteTimeSpan>中也拒绝 `dueTime` 后,第一个选中 <xref:System.DateTime.MaxValue>.

对于预定执行的某一项,登记一个有效的积极时期,并在持久工作成功后第一次调回提醒通知时不进行登记。

<a id="configure-reminder-storage"></a>

## 配置提醒存储

每个 silo 配置相同的提醒提供者,以便让所有权在整个集群之间移动。生产部署使用一个耐久的供应商,如 Azure 表、 ADO.NET、 Redis、亚马逊 DynamoDB或 [宇宙 DB](https://www.nuget.org/packages/Microsoft.Orleans.Reminders.Cosmos)。该模拟供应商储存了该集群存在期间的定义并支持当地发展和测试。

配置每个提供者, 通过其软件包提供的 API 来配置各个提供者。 编译的示例请参见 [配置亚马逊 DynamoDB 提醒](reminders/dynamodb.md) , 该示例独立配置 DynamoDB 组群和提醒存储。 当与 Aspire 组合资源时, 请见 [Orleans 和指定集成](../host/aspire-integration.md)。

提醒表是启动 silo 的一部分。提供者是否具备和是否一致决定服务能否装载、更新和调节登记。当家作主、更新和交付内部时,见 [提醒执行](../implementation/reminders.md) 。

<a id="poco-grains"></a>

## 波哥 grains

Grains 执行中 <xref:Orleans.IGrainBase> 直接使用相同 <xref:Orleans.GrainReminderExtensions> APPs. 注射 <xref:Orleans.Timers.IReminderRegistry> 当基础设施法规需要通过当前 grain 上下文。

<a id="troubleshoot-reminders"></a>

## 排除故障提醒

| 观察行为 | 运行时行为和行动 |
|---|---|
| 登记报告提醒服务未配置。 | 在登记提醒之前,对每个 silo 配置一个提醒提供者。 |
| 登记拒绝这一期限。 | 在 <xref:Orleans.Hosting.ReminderOptions.MinimumReminderPeriod?displayProperty=nameWithType>使用或超过正值期。 |
| 长时间的集束中断后滴答声到达 | 使 <xref:Orleans.Runtime.TickStatus> 的理论时间表与持久进展相协调,并处理杰出的商业工作。 |
| 在同一业务间隔内,回调执行一次以上。 | 在会籍变更期间提醒所有人。 使用每个间隔的应急关键值或耐久完成标记 。 |
| 定义在完全集重开后消失。 | 内线供应商将定义范围扩大到集群寿命期,配置一个耐久生产供应商。 |
| 高频工作需要分分钟间隔。 | 使用提醒来激活 grain, 然后登记活动阶段的 [grain 计时器](timers.md) 。 |
