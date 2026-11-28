---
title: 提醒器实现
description: 了解 Orleans 内部的提醒器所有权、持久化表协调、调度及投递行为。
ms.date: 08/21/2026
ms.topic: concept-article
---

<a id="reminder-implementation"></a>

# 提醒器实现

提醒器是持久化的调度计划，但其本地执行是易失的。提醒器行存储在 `IReminderTable` 中；负责 Grain 所处一致性哈希环范围的 Silo 将该行加载到 `LocalReminderService`，并在本地执行调度。表是注册信息的权威来源，而本地 `ReminderData` 状态是缓存和定时器。

<a id="registration-and-ownership"></a>

## 注册与所有权

`ReminderRegistry` 验证名称、首次触发时间、周期及配置的最小周期，然后将操作转发给 Grain 当前所有者的 Grain 服务。`LocalReminderService` 检查职责范围，对记录执行插入或更新，并使用返回的 ETag 协调本地条目。条件删除在 ETag 匹配时成功，并保留较新的注册。

提醒器服务是附着于一致性哈希环的 Grain 服务。成员资格变化时所有权随之变化；新所有者在下一次读取表时，根据持久化记录重建自己负责的调度任务。

<a id="refresh-and-reconciliation"></a>

## 刷新与协调

提醒器表成功启动后，服务周期性刷新其环范围。初始化期间错开读取时间并进行重试。每次刷新读取该范围，比较表序列与本地变更，启动或更新属于自己的且下一次触发落在 <xref:Orleans.Hosting.ReminderOptions.ReminderLoadingWindow> 内的条目，并停止已消失、移至其他位置或不再处于加载窗口内的条目。本地序列号防止并发注册或删除被较早的刷新结果覆盖。

提醒器表契约返回所有者一致性哈希范围内的所有记录。因此刷新会先传输并具体化全部所属记录，再应用加载窗口。常驻调度计划的数量随窗口内到期的提醒器数量增长，而刷新 I/O、反序列化及峰值内存分配则随该 Silo 所拥有的全部提醒器数量增长。

提醒器所有权通过周期性协调确定。成员资格收敛期间可能出现短暂的所有权重叠；ETag、职责检查及服务投递状态使重叠逐步收敛至当前所有者。

<a id="tick-delivery"></a>

## 触发事件投递

每个本地提醒器报告以下三种状态之一：已停止、运行中或墓碑状态。已停止的提醒器使其运行任务保持非活跃。运行中的提醒器根据存储的启动时间及周期计算下一次到期时间，使用提醒器的 `TimeProvider` 等待，并通过普通消息传递路径调用目标 Grain。墓碑状态记录停止原因，刷新协调过程会观察到该调度决策。

每次投递持有一个 `AdmissionGate` 令牌，直到回调完成并报告结果。停止服务时关闭准入，等待已准入的投递完成，然后停止本地提醒器循环。取消对停止操作的等待会释放调用方，但排空和清理仍继续。并发启动操作等待清理完成，再创建新的准入门；表协调根据持久化记录重建本地调度计划。

提供程序持久化调度计划，而所有者在内存中生成各次触发事件。所有者或进程发生故障后，下一任所有者重建调度计划，并根据当前时间计算下一次投递。应用根据持久化业务状态协调时间间隙，并通过幂等回调容忍重复执行。

<a id="storage-and-operational-boundaries"></a>

## 存储与运行边界

内存表仅适用于开发。生产环境的表提供所有权变更所需的持久性和并发行为，但其一致性及可用性特征仍取决于具体提供程序。表启动是 Silo 生命周期初始化的一部分；无法打开配置的表会阻止提醒器服务正常启动。

[提醒器指南](../grains/reminders.md) 介绍注册及应用行为。提醒器持久化、环上的所有权移动、刷新间隔以及回调失败共同决定实际观察到的调度行为。

源码：[`LocalReminderService`](https://github.com/dotnet/orleans/blob/main/src/Orleans.Reminders/ReminderService/LocalReminderService.cs)、[`ReminderRegistry`](https://github.com/dotnet/orleans/blob/main/src/Orleans.Reminders/ReminderService/ReminderRegistry.cs) 和 [`IReminderTable`](https://github.com/dotnet/orleans/blob/main/src/Orleans.Reminders/SystemTargetInterfaces/IReminderTable.cs)。
