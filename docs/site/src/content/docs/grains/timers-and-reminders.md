---
title: "Grain 定时器与提醒器"
description: "了解如何在 Orleans 中选择激活实例范围内的 Grain 定时器和持久化提醒器。"
ms.date: 08/21/2026
ms.topic: concept-article
navigation: hidden
---

<a id="grain-timers-and-reminders"></a>

# Grain 定时器与提醒器

Orleans 通过两种机制调度周期性的 Grain 工作，它们具有不同的归属范围和持久性保证：

| 机制 | 归属对象 | 调度计划存储位置 | 激活行为 | 典型执行频率 |
|---|---|---|---|---|
| [Grain 定时器](timers.md) | 单个 Grain 激活实例 | 激活实例的内存 | 在当前激活实例上执行，并随该实例的停用而终止 | 激活实例处于活动状态时频繁执行的工作 |
| [提醒器](reminders.md) | 单个逻辑 Grain | 已配置的提醒器提供程序 | 投递触发事件时激活 Grain，并在集群恢复后继续调度 | 以分钟、小时或天为周期的工作 |

当工作归属于当前激活实例时，选择 Grain 定时器。当调度计划归属于 Grain 标识，并且必须在激活实例及集群生命周期发生变化后继续存在时，选择提醒器。

<a id="grain-timers"></a>

## Grain 定时器

Grain 定时器在单个激活实例上将回调作为 Grain 的执行轮次执行。运行时在当前回调完成后才调度下一次触发，因此同一定时器的回调绝不会与自身重叠执行。

有关注册、回调调度、交错执行、激活实例生命周期、取消及故障排查，请参阅 [Grain 定时器](timers.md)。

<a id="timer-behavior"></a>

[定时器行为参考](timers.md#timer-behavior)介绍了由 <xref:Orleans.Runtime.GrainTimerCreationOptions> 控制的各项行为保证。

<a id="reminders"></a>

## 提醒器

提醒器为一个逻辑 Grain 持久化调度计划。提醒器的投递遵循正常的 Grain 请求调度规则，并在需要时激活 Grain。提供程序保存调度计划，而每次触发事件由运行时生成并投递。

有关注册、持久化调度、时间约束、提供程序配置、遗漏触发事件的补偿处理及故障排查，请参阅[提醒器](reminders.md)。

<a id="reminder-behavior"></a>

[提醒器投递行为参考](reminders.md#reminder-behavior)介绍了持久化的提醒器定义、非持久化的触发事件投递、重新激活及故障恢复。

<a id="reminder-timing-constraints"></a>

[提醒器时间约束](reminders.md#reminder-timing-constraints)定义了有效的首次触发延迟和执行周期。

<a id="configure-reminder-storage"></a>

## 配置提醒器存储

每个 Silo 配置一个提醒器提供程序。有关生产环境提供程序、开发环境配置及特定提供程序的指导，请参阅[配置提醒器存储](reminders.md#configure-reminder-storage)。

<a id="poco-grains"></a>

## POCO Grain

直接实现 <xref:Orleans.IGrainBase> 的 Grain 使用相同的定时器和提醒器扩展 API。[Grain 定时器](timers.md#poco-grains)和[提醒器](reminders.md#poco-grains)的专门页面介绍了可通过依赖注入获取的底层注册服务。
