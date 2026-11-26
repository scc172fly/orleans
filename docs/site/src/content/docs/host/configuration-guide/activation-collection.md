---
title: "活动收集和资源管理"
description: "管理空闲 Orleans 激活和内存压力。"
ms.date: 08/08/2026
ms.topic: concept-article
---

<a id="activation-collection-and-resource-management"></a>

# 活动收集和资源管理

grain 激活是目前代表 grain 身份的模拟实例。 Orleans 产生需求启动,并停止闲置启动,因此 silo 不会保留它曾经服务过的每一个 grain 。

激活收集与 .NET 垃圾收集分开。 Orleans 首先停止使用 grain ,并删除运行时间参考; .NET GC然后可以收回管理对象。

<a id="how-activation-collection-works"></a>

## 启动收集如何运作

激活在 <xref:Orleans.Configuration.GrainCollectionOptions.CollectionAge>闲置后有资格被收藏。 默认时间为 15 分钟。 Orleans 扫描 <xref:Orleans.Configuration.GrainCollectionOptions.CollectionQuantum>, 默认时间为 1分钟, 所以取消不会在一瞬间安排 。

收到 grain 电话、 提醒和流事件计为活动。 发送的通话和任意应用 I/ O 不保持用于收集目的的激活活动。 计时器不默认保持激活活动, 而是以 <xref:Orleans.Runtime.GrainTimerCreationOptions.KeepAlive> 重置在每次回调后闲置启动而创建的计时器 。

<a id="configuration"></a>

## 配置

配置全球时代和目标覆盖符:

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="activation_collection":::

相对于整个集群范围的增加而言,更倾向于特定类型的变化。 较长的行业记忆用于减少激活和国营装载作业。

<a id="explicit-control-of-activation-collection"></a>

## 明确控制激活收藏

<a id="expedite-activation-collection"></a>

### 快速启动收集

在当前启动时请求在当前转弯后停止运行时, 通知 <xref:Orleans.Grain.DeactivateOnIdle*> :

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="deactivate_on_idle":::

此例方法仅从 grain 执行中调用。 它要求取消操作; 不等待激活停止。 被调用过的电话会被传送到新的或现有的激活中 。

<a id="delay-activation-collection"></a>

### 延迟激活收藏

呼叫 <xref:Orleans.Grain.DelayDeactivation*> 将一个活动从闲置收藏中保持至少一段时间 :

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="delay_deactivation":::

延迟会与配置的收集时间相合并; 它不会替换它。 激活只有在延迟过后才能被允许闲置收藏, 而激活的时间在收集时间已空闲之后才能被允许。 采集过程会在较晚的扫描中进行, 因此这些时间是较低的界限而不是时间表 。

每次通话都设定了自通话起的新期限:

| 参数 | 效果效果 |
|---|---|
| 积极持续期 | 防止闲置收藏, 直到至少这一持续时间结束 。 |
| <xref:System.TimeSpan.Zero> | 取消上次的延迟, 并返回到配置的收集年龄 。 |
| <xref:System.Threading.Timeout.InfiniteTimeSpan> 或 <xref:System.TimeSpan.MaxValue> | 无限期延迟闲置收藏 。 |

假设收集时间为10分钟,

| 活动活动活动 | 最严重资格 |
|---|---|
| 拨打 `DelayDeactivation` ,20分钟后在0分,然后不打电话。 | 20分钟 |
| 以五分钟时间拨打 `DelayDeactivation` ,然后不要打电话。 | 十分钟,因为延迟无法缩短配置的收集年龄。 |
| 以20分钟的0分20分钟打 `DelayDeactivation` ,然后在第7分接到普通 grain 电话。 | 20分钟,延迟不会滑动 当其他电话到达。 |
| 调调 `DelayDeactivation` 5分钟,0分,然后接收普通 grain 7分钟后呼叫 | 不超过第17分钟,在新的10分钟空闲时间之后。 |

<xref:Orleans.Grain.DeactivateOnIdle*> 优先于延迟。延迟解除是优化,而不是持久性或安置保证。它不会将激活附加到 silo,故障、关闭、迁移、明确停止和内存压力仍然可以消除引爆。

<a id="how-to-deactivate-a-specific-grain-identity"></a>

### 如何禁用特定 grain 身份

守则必须针对特定 grain 身份以外 grain 能够向公众进行引用 <xref:Orleans.Core.Internal.IGrainManagementExtension> 延长和拨号 <xref:Orleans.Core.Internal.IGrainManagementExtension.DeactivateOnIdle*>:

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="deactivate_grain_externally":::

这是请求, 不是等待解除功能的呼叫。 方法在请求取消 grain 身份后立即返回; 队列中的呼叫被转发到新的或现有的激活。 如果没有当前激活, 以后的呼叫可以重新启动 grain。

测试主机助手是用于测试的方便性APS:

- <xref:Orleans.TestingHost.TestCluster.DeactivateAsync*> 使用公共管理扩展,然后等待服务器完成当前激活的失效。
- <xref:Orleans.TestingHost.InProcessTestCluster.DeactivateAsync*> 直接停止当前 grain 上下文的状态,等待解除状态完成。

这些助手很有价值 因为他们在测试中制造了解除功能的确定性, 但是他们并不是一个不同的运行时间。

下 <xref:Orleans.Grain.DeactivateOnIdle*> 方法也可用于内部 grain 代码时的 grain 其目前启动活动应终止:

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="explicit_deactivate_grain":::

只有当有终止激活的域或操作理由时(例如,在完成一次性工作流程后或释放昂贵的内存资源后)才使用明确的禁用。 不要将它用于普通生命周期管理;闲置收集是默认和首选行为。

<a id="what-idle-means-for-collection"></a>

### 收集“ idle” 是什么意思

启用在配置空闲窗口期间未处理入境工作时是空闲的。 导入 grain 时间回回回只有在定时器创建时才会重新设置空闲 <xref:Orleans.Runtime.GrainTimerCreationOptions.KeepAlive>.

`DeactivateOnIdle` 请求当当前转折结束时停止使用; 排队的电话被传送到新的或现有的激活。 稍后拨打同一 grain 身份后, 可以在任何兼容的 silo上重新启动它 。

<a id="cautions"></a>

### 注意

- 将禁用作为最佳努力清理处理。 它可以被突然的流程终止和一些失败路径跳过 。
- 不仅在以下国家,而且作为正常业务的一部分,作为 <xref:Orleans.Grain.OnDeactivateAsync*>.
- 不要使用显性失能代替内存压力调试。 使用基于测量的收集设置和缩放 。

<a id="keep-alive"></a>

### 活着

仅在启动应免用正常闲置收藏时才应用 <xref:Orleans.KeepAliveAttribute> 来实施 grain :

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="keep_alive_grain":::

保持生命活力的激活仍然消耗着记忆 无法替代耐久状态

<a id="memory-based-activation-shedding"></a>

## 基于记忆的激活停止

<a id="enable-memory-based-activation-shedding"></a>

Orleans 当进程内存超过配置百分比时可以卸下激活 :

默认值为:

<a id="configuration-options"></a>

### 配置选项

| 选项 | 默认默认 |
|---|---:|
| <xref:Orleans.Configuration.GrainCollectionOptions.EnableActivationSheddingOnMemoryPressure> | `false` |
| <xref:Orleans.Configuration.GrainCollectionOptions.MemoryUsageLimitPercentage> | `80` |
| <xref:Orleans.Configuration.GrainCollectionOptions.MemoryUsageTargetPercentage> | `75` |
| <xref:Orleans.Configuration.GrainCollectionOptions.MemoryUsagePollingPeriod> | 5秒5秒 |

<a id="how-it-works"></a>

### 如何运作

当启用时, Orleans 估计有多少启动到停止激活,以便从极限向目标移动, 优先使用老的激活。 内存压力可以取代正常的保持时间, 因为保护程序比保持优化更重要 。

调试百分比阈值之前设定容器或处理内存限。 在目标、 限制和平台的硬限之间留出足够的空间, 以便取消回击、 状态写作和 GC 完成 。

<a id="activation-and-deactivation-timeouts"></a>

## 激活和停止启动超时

<xref:Orleans.Configuration.GrainCollectionOptions.ActivationTimeout> and <xref:Orleans.Configuration.GrainCollectionOptions.DeactivationTimeout> both default to 30 seconds. When a timeout elapses, Orleans cancels the <xref:System.Threading.CancellationToken> passed to <xref:Orleans.Grain.OnActivateAsync*>, <xref:Orleans.Grain.OnDeactivateAsync*>, and grain lifecycle participants, then waits for that code to complete. The activation keeps its grain directory registration and activation-scoped services until those callbacks return. If the activation timeout elapses, activation fails after the callbacks return. If the deactivation timeout elapses, deactivation continues after the callbacks return.

将信号传递到状态访问和依赖电话, 这样取消后回调会迅速完成。 保持这些回拨通常比超时快得多 。

激活采集器等待每批的停用超时, 然后继续收集其他闲置的启动。 充电活动仍然在运行着, 保留服务并注册到解除使用完成为止 。

<a id="tune-from-measurements"></a>

## 测量的表线

至少音轨 :

- 以 grain 类型计起激活率和启动/解除能力。
- 状态载荷和州-月延。
- 工序、管理下的堆积尺寸和分配率。
- GC时间和暂停期。
- 记忆压力退缩事件和失能故障。

如果激活的弹道高,但内存健康,请提高受影响类型的年龄。如果记忆保持高,减少保留应用状态,缩短选定的年龄,缩小规模或允许存储压力延缓。配置 [GC 服务器服务器](configuring-garbage-collection.md) 排排 silo 进程。
