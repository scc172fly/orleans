---
title: "Grain 计时器"
description: "将 Orleans grain 转动时的周期工作排入启动范围。"
ms.date: 10/15/2026
ms.topic: concept-article
---

<a id="grain-timers"></a>

# Grain 计时器

A grain 定期工作 grain 激活。 每个回调执行时, 以 A grain 启动并随后 Orleans 请求安排、跟踪和 grain 运行时拥有激活寿命的定时器, 当启动失效时丢弃它 。

使用 a grain 使用一个 [提醒提醒](reminders.md) 当附表属于逻辑 grain 并且必须活下来活化或集群生命周期变化。

要作为 grain 请求再运行一次回调,请使用 <xref:Orleans.GrainBaseExtensions.RunRequestAsync*>。见 [请求安排](request-scheduling.md#run-background-work-as-a-grain-request)。

<a id="register-a-grain-timer"></a>

## 注册 grain 计时器

登记计时器 <xref:Orleans.GrainBaseExtensions.RegisterGrainTimer*>. <xref:Orleans.Grain.RegisterTimer*> 已经过时。

:::code language="csharp" source="../snippets/compiled/Grains/WorkersAndTimersSnippets.cs" id="grain_timer":::

<xref:Orleans.GrainBaseExtensions.RegisterGrainTimer*> 返回 <xref:Orleans.Runtime.IGrainTimer>。当激活需要更改或停止计划时, 保留控点 。

<a id="timer-behavior"></a>

## 计时器行为

<xref:Orleans.Runtime.GrainTimerCreationOptions> controls the initial schedule and callback behavior:

| 财产 | 默认默认 | 运行时行为 |
|---|---:|---|
| <xref:Orleans.Runtime.GrainTimerCreationOptions.DueTime> | 需要 | 延迟第一次调回。 <xref:System.TimeSpan.Zero> 立即安排时间, <xref:System.Threading.Timeout.InfiniteTimeSpan> 使计时器暂停。 |
| <xref:Orleans.Runtime.GrainTimerCreationOptions.Period> | 需要 | 在当前回调完成后延迟下次回调。 <xref:System.Threading.Timeout.InfiniteTimeSpan> 创建一个单列的回调 。 |
| <xref:Orleans.Runtime.GrainTimerCreationOptions.Interleave> | `false` | 应用 grain的正常恢复规则。 `true` 允许回调与其他 grain 电话和计时器交叉接驳。 |
| <xref:Orleans.Runtime.GrainTimerCreationOptions.KeepAlive> | `false` | 控制每个回调是否延长激活的闲置寿命。 |

<a id="callback-scheduling"></a>

### 召回日程安排

计时回调从不重叠。 Orleans 等待回调任务完成,然后在排好下次回调之前测量时间段。 因此回调时间会增加回调开始之间的间隔 。

时间回调是仅针对本地的信号, 用于激活它们。 他们参加正常的转班日程安排, 并保留注册的激活时间 。

<a id="interleaving"></a>

### 间隔

与 <xref:Orleans.Runtime.GrainTimerCreationOptions.Interleave> 设置至 `false`,回调后 grain的再生配置 grain 方法呼叫。 grain 进程调回时,不与其他请求连接。重新输入 grain 可根据日程安排规则中断回调。

设定 <xref:Orleans.Runtime.GrainTimerCreationOptions.Interleave> 至 `true` 当召唤能安全地观察的时候, grain 状态在等待时发生变化, 而其他旋转执行 。

<a id="activation-lifetime"></a>

### 启动寿命

计时回调使激活的闲置寿命不变。 Orleans 能够收集其它闲置的激活, 从而结束激活的所有定时器 。

设置 <xref:Orleans.Runtime.GrainTimerCreationOptions.KeepAlive> 到 `true`, 每次回调都会延长激活寿命。 比配置的闲置收集期短的一段时间会通过连续回调来保持激活活动。 反复回调仍然允许在选中之间进行收藏 。

<a id="change-or-stop-a-timer"></a>

## 更改或停止定时器

调用 <xref:Orleans.Runtime.IGrainTimer.Change*> 以替换到期时间和期间。新的到期时间安排下一个回调,新时期则在回调完成后适用。在回调结束后,在回调中作出的更改生效。

丢弃 <xref:Orleans.Runtime.IGrainTimer> 以取消调回牌并停止未来的调回。 Orleans 也取消调回牌,并在激活开始停用时处置计时器。

<a id="handle-callback-failures"></a>

## 处理回调失败

Orleans 记录例外被计时器回调返回, 并计划在配置的期间后下次回调。 在允许例外逃跑之前保持应用程序状态一致, 当恢复必须跨越激活失败时使用 grain 状态或其他耐久存储 。

调回 <xref:System.Threading.CancellationToken> 信号计时器处理和启动关闭。 在非同步工作中观察它, 这样解除功能就能迅速完成 。

<a id="poco-grains"></a>

## 波哥 grains

Grains 执行中 <xref:Orleans.IGrainBase> 直接使用 <xref:Orleans.GrainBaseExtensions.RegisterGrainTimer*>注射 <xref:Orleans.Timers.ITimerRegistry> 基础设施法规需要低级别登记时, <xref:Orleans.Runtime.IGrainContext>.

<a id="troubleshoot-grain-timers"></a>

## 排除故障计时器 grain

| 观察行为 | 运行时行为和行动 |
|---|---|
| 定时器在激活收藏或 silo 故障后结束。 | 定时器属于激活。 在启动设置过程中注册, 或者使用 [提醒提醒](reminders.md) 来保存激活的更改 。 |
| 回召开始漂移 时间间隔超过墙时段 。 | Orleans 测量回调完成后的时期。缩短回调时间,调整时间或按长效应用状态的墙上时钟表模式。 |
| 其他 grain 呼叫在等待回拨时执行。 | grain 是重新输入, 或定时器启用 <xref:Orleans.Runtime.GrainTimerCreationOptions.Interleave>。 保护等待点之间的异位数或使用非间断时间排程 。 |
| 闲置激活活动仍然在运行。 | <xref:Orleans.Runtime.GrainTimerCreationOptions.KeepAlive> 延长每次回调的寿命。 当收藏应该遵循正常闲置超时后, 请将其设为 `false` 。 |
| 重复回调的例外情况屡次出现。 | Orleans 记录例外情形并继续执行时间表。让回调从耐久状态中趋同,然后解决潜在的故障。 |
