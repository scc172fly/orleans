---
title: "提醒执行"
description: "解释提醒所有人、常桌对账、时间安排和交付行为 Orleans."
ms.date: 08/21/2026
ms.topic: concept-article
---

<a id="reminder-implementation"></a>

# 提醒执行

提醒是耐久的日程时间表,当地执行时变化不定。 `IReminderTable`; ; 和 silo 负责者负责的 grain划入 `LocalReminderService` 并在当地运行时间表。表格是真实的登记来源,而本地 `ReminderData` 状态是缓存和计时器。

<a id="registration-and-ownership"></a>

## 登记和所有权

`ReminderRegistry` 验证名称、到期时间、期限和将操作转交 grain 服务 grain目前的主人 `LocalReminderService` 进行责任检查, 上移行, 并使用返回的 Etag 来调节本地条目。 有条件删除成功匹配 ETag 并保存更新的注册 。

提醒服务是附在一致环上的 grain 服务,当会员身份变更时所有权就发生变化;新的所有人从下一行的“长列”中重新确定责任。

<a id="refresh-and-reconciliation"></a>

## 刷新和对账

提醒表成功启动后, 服务会定期刷新环范围。 读取时间被错开并在初始化期间重新尝试。 每个更新都读取范围, 将表格序列与本地变异进行比较, 开始或更新自有条目, 其下个选项在 <xref:Orleans.Hosting.ReminderOptions.ReminderLoadingWindow>之内, 并停止丢失、 移到别处或不再属于装入窗口的条目。 一个本地序列阻止同时登记或删除被旧的更新结果覆盖 。

提醒表合同返回所有者统一频带范围内的每一行。 因此, 在应用装入窗口前, 重新更新并实现所有自有行的传输。 居民时间表表在窗口内应加提醒, 而更新 I/ O、 取消序列化和最高分配等级则包含所有由 silo拥有的提醒 。

提醒所有制使用定期调节。 在会员合并期间,可以出现短暂的拥有权重叠;ETags、责任检查和服务的提供国将重叠情况集中到目前的所有人身上。

<a id="tick-delivery"></a>

## 滴滴滴滴滴滴滴滴滴滴滴滴滴滴滴滴滴滴滴

本地提醒通知报告三个州中的一个国家:停止、运行或墓碑。 停止的提醒使其运行任务保持不活动。 从存储的开始时间和时期算出下一个到期时间, 使用提醒等待 `TimeProvider`,并援引目标 grain 墓碑记录了停止的理由, 重新调节时却观察了日程安排决定。

发送时都拥有一个通过回调完成和结果报告而发出的 `AdmissionGate` 标记。 停止服务会关闭接收,等待被接送完,然后停止本地提醒循环。 取消停止等候会释放拨打者,同时排水和清理工作继续。 同时开始在创建新大门之前完成清理,表格对账则从耐用行重建本地时间表。

供应商长期存储时间表, 而所有者会在记忆中生成单个的勾选。 在所有人或进程失败后, 下一个所有人将重建时间表并计算从目前开始的下一批交货。 应用程序可以调节耐用商业状态的时间差, 并且一无所有回调能容忍重复执行 。

<a id="storage-and-operational-boundaries"></a>

## 储存和作业边界

生产表提供所有权变更所需的耐久性和同货币行为,但其一致性和可用性特点仍由供应商决定。 silo 生命周期初始化; 未打开配置的表格, 无法启动正常提醒服务 。

[提醒提示指南](../grains/reminders.md) 涵盖登记和应用行为。提醒、环状运动、更新间隔和回击失败决定了观察到的时间安排行为。

来源: [`LocalReminderService`](https://github.com/dotnet/orleans/blob/main/src/Orleans.Reminders/ReminderService/LocalReminderService.cs)、 [`ReminderRegistry`](https://github.com/dotnet/orleans/blob/main/src/Orleans.Reminders/ReminderService/ReminderRegistry.cs)和 [`IReminderTable`](https://github.com/dotnet/orleans/blob/main/src/Orleans.Reminders/SystemTargetInterfaces/IReminderTable.cs)。
