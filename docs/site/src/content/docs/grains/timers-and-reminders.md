---
title: "Grain 计时器和提醒通知"
description: "在激活范围之间选择 grain 和《可持久的催问和 催交的月时间和 月度的月 Orleans."
ms.date: 08/21/2026
ms.topic: concept-article
navigation: hidden
---

<a id="grain-timers-and-reminders"></a>

# Grain 计时器和提醒通知

Orleans 时间表定期 grain 通过两个机制开展工作,这两个机制具有不同的所有权和持久性保证:

| 机制机制机制 | 所有人 | 定期储存 | 激活行为 | 典型的连续 |
|---|---|---|---|---|
| [Grain 计时器](timers.md) | 1 grain 激活 | 激活内存 | 执行当前激活并结束此激活 | 激活活动时经常工作 |
| [提醒提醒](reminders.md) | 一个逻辑 grain | 配置提醒服务提供方 | 执行时激活 grain ,当一个滴答交付并在集集恢复后恢复 | 以分钟、小时或天数计量的工作 |

当工作属于当前激活时选择 grain 计时器。 当时间表属于 grain 身份时选择提醒, 并且必须在激活和集束生命周期变化中生存下来 。

<a id="grain-timers"></a>

## Grain 计时器

Grain 计时器执行回调, 以一个激活时间打开 grain 。 运行时间安排在当前回调完成后下一个勾选, 因此计时器回调不会重叠 。

登记、回召时间安排、中继、启动寿命、取消和排除故障见 [Grain 计时器](timers.md) 。

<a id="timer-behavior"></a>

[时间器行为参考](timers.md#timer-behavior) 描述 <xref:Orleans.Runtime.GrainTimerCreationOptions>控制的担保。

<a id="reminders"></a>

## 提醒提醒

提醒始终是逻辑 grain的时间表。提醒发送遵循正常的 grain 请求时间表,必要时启动 grain 。在每次滴子生成和交付时,提供者保存该时间表。

登记、耐久时间安排、时间限制、供应商配置、误点调节和排除故障见 [提醒提醒](reminders.md) 。

<a id="reminder-behavior"></a>

[催发催发通知参考](reminders.md#reminder-behavior) 解释耐久定义、波动滴答交付、恢复和恢复。

<a id="reminder-timing-constraints"></a>

[提醒提醒时间限制](reminders.md#reminder-timing-constraints) 定义有效到期时间和期限。

<a id="configure-reminder-storage"></a>

## 配置提醒存储

每个 silo 配置一个提醒提供者。关于生产提供者、开发配置和具体提供者指南,见 [配置提醒存储](reminders.md#configure-reminder-storage) 。

<a id="poco-grains"></a>

## 波哥 grains

Grains 执行中 <xref:Orleans.IGrainBase> 直接使用同时间器和提醒扩展 APIs。 [grain 计时器](timers.md#poco-grains) 和 [提醒提醒](reminders.md#poco-grains) 说明通过依赖注射提供的较低级别的登记册。
