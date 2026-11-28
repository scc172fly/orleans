---
title: 提醒器
description: 调度能够跨越激活实例和集群生命周期变化的持久化周期性 Grain 工作。
ms.date: 08/21/2026
ms.topic: concept-article
---

<a id="reminders"></a>

# 提醒器

提醒器为一个逻辑 Grain 保存周期性调度计划。配置的提醒器提供程序负责保存其定义，Orleans 通过正常的 Grain 请求调度机制投递触发事件。如果 Grain 当前没有激活实例，触发事件会使其激活。

对于调度计划必须在激活实例变化和集群重启后仍然存在的工作，请使用提醒器。提醒器适用于以分钟、小时或天为单位的周期。提醒器可以唤醒 Grain，再由 Grain 创建 [Grain 定时器](timers.md)，执行更细粒度的激活实例范围内的工作。

<a id="receive-reminder-ticks"></a>

## 接收提醒器触发事件

接收提醒器通知的 Grain 实现 <xref:Orleans.IRemindable>：

:::code language="csharp" source="../snippets/compiled/Grains/WorkersAndTimersSnippets.cs" id="remindable_report_grain":::

Orleans 使用提醒器名称和 <xref:Orleans.Runtime.TickStatus> 调用 <xref:Orleans.IRemindable.ReceiveReminder*>。该回调作为 Grain 请求执行，并遵循 Grain 的调度及可重入规则。

当提醒器工作可能超过 Grain 调用超时时，请使用 [长时间运行的提醒器示例](../how-to/long-running-reminders.md)，启动一个可重新启动的工作执行器并及时返回。

<a id="register-or-update-a-reminder"></a>

## 注册或更新提醒器

在 Grain 中注册命名提醒器：

:::code language="csharp" source="../snippets/compiled/Grains/WorkersAndTimersSnippets.cs" id="register_reminder":::

<xref:Orleans.GrainReminderExtensions.RegisterOrUpdateReminder*> 创建持久化定义。再次注册同一名称时会替换其首次触发等待时间和周期。

返回的 <xref:Orleans.Runtime.IGrainReminder> 是当前注册的句柄。将提醒器名称持久化到应用状态，在激活之后按需重新获取当前句柄。

<a id="retrieve-or-remove-a-reminder"></a>

## 获取或移除提醒器

获取当前句柄并注销提醒器：

:::code language="csharp" source="../snippets/compiled/Grains/WorkersAndTimersSnippets.cs" id="unregister_reminder":::

使用 <xref:Orleans.GrainReminderExtensions.GetReminder*> 获取一个命名提醒器，使用 <xref:Orleans.GrainReminderExtensions.GetReminders*> 获取 Grain 注册的所有提醒器。

<a id="reminder-behavior"></a>

## 提醒器行为

提醒器提供程序持久化保存每条定义。负责处理的 Silo 加载定义，根据已持久化的开始时间和周期计算各次触发时间，并向 Grain 发送触发请求。

单次触发消息是临时的。集群不可用或所有权迁移可能导致某次计划触发未能投递，而持久化定义仍会继续产生后续触发事件。所有权收敛期间也可能发生重复回调。因此，每次回调都应按幂等方式处理，并根据持久化业务状态补偿或核对工作进度。

<xref:Orleans.Runtime.TickStatus.FirstTickTime> 和 <xref:Orleans.Runtime.TickStatus.Period> 确定理论调度计划。<xref:Orleans.Runtime.TickStatus.CurrentTickTime> 记录运行时开始此次投递的时间。应用可将这些值与持久化进度比较，以发现并补偿遗漏的触发事件。

Orleans 将提醒器通知投递到该 Grain 标识的一个激活实例。必要时，投递会激活 Grain，包括前一个激活实例已被回收或其所在 Silo 已停止的情况。

<a id="reminder-timing-constraints"></a>

## 提醒器时间约束

提醒器注册接受满足以下限制的调度计划：

- `dueTime` 大于或等于 <xref:System.TimeSpan.Zero>。零表示立即调度首次触发。
- `dueTime` 必须处于注册时间起剩余的 <xref:System.DateTime> 可表示范围内。
- `period` 为正数，且不小于 <xref:Orleans.Hosting.ReminderOptions.MinimumReminderPeriod?displayProperty=nameWithType>。
- 默认最小周期为一分钟。

运行时拒绝负数及 <xref:System.Threading.Timeout.InfiniteTimeSpan>，还会拒绝使首次触发晚于 <xref:System.DateTime.MaxValue> 的 `dueTime`。

如果只需要执行一次计划任务，请注册有效的正周期，并在首次回调中持久化工作成功后注销提醒器。

<a id="configure-reminder-storage"></a>

## 配置提醒器存储

所有 Silo 应配置相同的提醒器提供程序，使所有权能够在集群中迁移。生产部署使用持久化提供程序，例如 Azure Table、ADO.NET、Redis、Amazon DynamoDB 或 [Cosmos DB](https://www.nuget.org/packages/Microsoft.Orleans.Reminders.Cosmos)。内存提供程序只在集群生存期内保留定义，适用于本地开发和测试。

使用各提供程序所属包提供的 API 进行配置。参阅 [配置 Amazon DynamoDB 提醒器](reminders/dynamodb.md)，其中包含分别配置 DynamoDB 集群成员服务及提醒器存储的可编译示例。如果使用 Aspire 组合资源，请参阅 [Orleans 与 Aspire 集成](../host/aspire-integration.md)。

提醒器表是 Silo 启动流程的一部分。提供程序的可用性和一致性决定服务能否加载、更新和核对注册记录。有关所有权、刷新和投递内部机制，请参阅 [提醒器实现](../implementation/reminders.md)。

<a id="poco-grains"></a>

## POCO Grain

直接实现 <xref:Orleans.IGrainBase> 的 Grain 使用相同的 <xref:Orleans.GrainReminderExtensions> API。如果基础设施代码需要通过当前 Grain 上下文执行更底层的访问，请注入 <xref:Orleans.Timers.IReminderRegistry>。

<a id="troubleshoot-reminders"></a>

## 提醒器故障排查

| 观察到的现象 | 运行时行为及处理方式 |
|---|---|
| 注册时报告未配置提醒器服务。 | 注册提醒器前，在每个 Silo 上配置同一提醒器提供程序。 |
| 注册拒绝指定周期。 | 使用不小于 <xref:Orleans.Hosting.ReminderOptions.MinimumReminderPeriod?displayProperty=nameWithType> 的正周期。 |
| 集群中断很长时间后收到触发事件。 | 将 <xref:Orleans.Runtime.TickStatus> 中的理论调度计划与持久化进度核对，处理尚未完成的业务工作。 |
| 同一业务时间区间内执行了多次回调。 | 集群成员变化期间提醒器所有权需要收敛。为每个区间使用幂等键或持久化完成标记。 |
| 整个集群重启后定义消失。 | 内存提供程序只在集群生存期内保留定义。请配置持久化的生产提供程序。 |
| 高频工作需要低于一分钟的时间间隔。 | 使用提醒器激活 Grain，然后在活动阶段注册 [Grain 定时器](timers.md)。 |
