---
title: 激活实例回收与资源管理
description: 管理空闲 Orleans 激活实例与内存压力。
ms.date: 08/08/2026
ms.topic: concept-article
---

<a id="activation-collection-and-resource-management"></a>

# 激活实例回收与资源管理

Grain 激活实例是当前代表某个 Grain 标识的内存实例。Orleans 按需创建激活实例并停用空闲实例，因此 Silo 无须一直保留它曾经服务过的每个 Grain。

激活实例回收与 .NET 垃圾回收不同。Orleans 先停用 Grain 并移除运行时引用；之后 .NET GC 才可以回收托管对象。

<a id="how-activation-collection-works"></a>

## 激活实例回收的工作方式

激活实例空闲达到 <xref:Orleans.Configuration.GrainCollectionOptions.CollectionAge> 后具备被回收的资格。默认值为 15 分钟。Orleans 按 <xref:Orleans.Configuration.GrainCollectionOptions.CollectionQuantum> 扫描，默认间隔为 1 分钟，因此停用并不会被安排在某个精确时刻。

传入的 Grain 调用、提醒器触发及流事件计入活动。传出调用和任意应用程序 I/O 不会使激活实例在回收机制中保持活动。默认情况下定时器不会保持激活实例活动，但使用 <xref:Orleans.Runtime.GrainTimerCreationOptions.KeepAlive> 创建的定时器会在每次回调后重置激活实例的空闲计时。

<a id="configuration"></a>

## 配置

配置全局空闲时限和有针对性的覆盖设置：

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="activation_collection":::

优先按类型调整，而不是在整个集群提高时限。较长的时限会以更多内存占用换取更少的激活和状态加载操作。

<a id="explicit-control-of-activation-collection"></a>

## 显式控制激活实例回收

<a id="expedite-activation-collection"></a>

### 加快激活实例回收

当当前激活实例应在当前执行轮次结束后请求停用时，调用 <xref:Orleans.Grain.DeactivateOnIdle*>：

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="deactivate_on_idle":::

此实例方法只能从 Grain 实现内部调用。它请求停用，但不会等待激活实例停止。已排队的调用将转发给新的或已有的激活实例。

<a id="delay-activation-collection"></a>

### 延迟激活实例回收

调用 <xref:Orleans.Grain.DelayDeactivation*>，可至少在一段时间内防止空闲激活实例被回收：

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="delay_deactivation":::

此延迟与配置的回收空闲时限共同生效，而不是替代它。只有延迟已届满且激活实例已空闲达到其回收时限时，才具备空闲回收资格。回收发生在后续扫描中，因此这些时间表示下限而不是预定时刻。

每次调用都从调用时刻起设置新的截止时间：

| 参数 | 效果 |
|---|---|
| 正时长 | 至少在该时长经过之前阻止空闲回收。 |
| <xref:System.TimeSpan.Zero> | 取消先前的延迟，恢复为配置的回收时限。 |
| <xref:System.Threading.Timeout.InfiniteTimeSpan> 或 <xref:System.TimeSpan.MaxValue> | 无限期延迟空闲回收。 |

例如，假设回收空闲时限为 10 分钟，并忽略扫描延迟：

| 活动 | 最早具备回收资格的时刻 |
|---|---|
| 在第 0 分钟以 20 分钟为参数调用 `DelayDeactivation`，此后没有任何调用。 | 第 20 分钟。 |
| 在第 0 分钟以 5 分钟为参数调用 `DelayDeactivation`，此后没有任何调用。 | 第 10 分钟，因为延迟不能缩短配置的回收时限。 |
| 在第 0 分钟以 20 分钟为参数调用 `DelayDeactivation`，在第 7 分钟接收普通 Grain 调用。 | 第 20 分钟。其他调用到来时不会使延迟截止时间顺延。 |
| 在第 0 分钟以 5 分钟为参数调用 `DelayDeactivation`，在第 7 分钟接收普通 Grain 调用。 | 不早于第 17 分钟，即新的 10 分钟空闲期之后。 |

<xref:Orleans.Grain.DeactivateOnIdle*> 优先于延迟设置。延迟停用是一项优化，而非持久性或放置保证。它不会将激活实例固定到 Silo，故障、关闭、迁移、显式停用及内存压力仍可能导致激活实例被移除。

<a id="how-to-deactivate-a-specific-grain-identity"></a>

### 如何停用特定 Grain 标识

在 Grain 外部需要针对特定 Grain 标识的代码，可以将引用转换为公共扩展接口 <xref:Orleans.Core.Internal.IGrainManagementExtension>，并调用 <xref:Orleans.Core.Internal.IGrainManagementExtension.DeactivateOnIdle*>：

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="deactivate_grain_externally":::

这是一个请求，而不是等待停用完成的调用。该方法在请求停用该 Grain 标识后立即返回；已排队的调用将转发给新的或已有的激活实例。如果当前没有激活实例，后续调用可以重新激活 Grain。

测试宿主辅助方法是便于测试的 API：

- <xref:Orleans.TestingHost.TestCluster.DeactivateAsync*> 使用公共管理扩展，然后等待服务器完成当前激活实例的停用。
- <xref:Orleans.TestingHost.InProcessTestCluster.DeactivateAsync*> 直接停用当前 Grain 上下文，并等待此次停用完成。

这些辅助方法很有价值，因为它们使测试中的停用具有确定性，但并不是另一种运行时能力。

当 Grain 自身决定当前激活实例应结束时，也可以在 Grain 代码内部使用 <xref:Orleans.Grain.DeactivateOnIdle*> 方法：

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="explicit_deactivate_grain":::

仅在存在业务领域或运维上的理由时显式停用激活实例（例如完成一次性工作流程后，或释放成本较高的内存资源时）。不要将其用于普通生命周期管理；空闲回收是默认且首选的行为。

<a id="what-idle-means-for-collection"></a>

### 回收机制中“空闲”的含义

当激活实例在配置的空闲时间窗口内没有处理传入工作时，即处于空闲状态。传入的 Grain 调用、提醒器和流事件会重置空闲计时。传出调用和任意本地工作不会。仅当使用 <xref:Orleans.Runtime.GrainTimerCreationOptions.KeepAlive> 创建定时器时，定时器回调才重置空闲计时。

`DeactivateOnIdle` 在当前执行轮次结束时请求停用；排队中的调用将转发到新的或已有的激活实例。后续对同一 Grain 标识的调用可以在任何兼容的 Silo 上重新激活它。

<a id="cautions"></a>

### 注意事项

- 将停用视为尽力而为的清理。进程突然终止或某些故障路径可能跳过它。
- 将重要状态作为正常操作的一部分进行持久化，而不是仅在 <xref:Orleans.Grain.OnDeactivateAsync*> 中进行。
- 不要使用显式停用代替内存压力调优。根据测量结果使用回收设置和扩缩容。

<a id="keep-alive"></a>

### 保持活动

仅当激活实例需要免于普通空闲回收时，才在 Grain 实现上应用 <xref:Orleans.KeepAliveAttribute>：

:::code language="csharp" source="../snippets/hosting/HostingExamples.cs" id="keep_alive_grain":::

保持活动的激活实例仍然消耗内存，不能代替持久状态。

<a id="memory-based-activation-shedding"></a>

## 基于内存的激活实例削减

<a id="enable-memory-based-activation-shedding"></a>

当进程内存超过配置的百分比时，Orleans 可以削减激活实例：

默认值如下：

<a id="configuration-options"></a>

### 配置选项

| 选项 | 默认值 |
|---|---:|
| <xref:Orleans.Configuration.GrainCollectionOptions.EnableActivationSheddingOnMemoryPressure> | `false` |
| <xref:Orleans.Configuration.GrainCollectionOptions.MemoryUsageLimitPercentage> | `80` |
| <xref:Orleans.Configuration.GrainCollectionOptions.MemoryUsageTargetPercentage> | `75` |
| <xref:Orleans.Configuration.GrainCollectionOptions.MemoryUsagePollingPeriod> | 5 秒 |

<a id="how-it-works"></a>

### 工作原理

启用后，Orleans 估算需要停用多少激活实例才能使内存占用从限制值向目标值降低，并优先选择较早的激活实例。内存压力可以覆盖正常的保持活动时限，因为保护进程比保留优化更重要。

在调整百分比阈值之前设置容器或进程的内存限制。在目标值、限制值和平台硬限制之间留出足够空间，让停用回调、状态写入和 GC 能够完成。

<a id="activation-and-deactivation-timeouts"></a>

## 激活与停用超时

<xref:Orleans.Configuration.GrainCollectionOptions.ActivationTimeout> 和 <xref:Orleans.Configuration.GrainCollectionOptions.DeactivationTimeout> 的默认值均为 30 秒。超时届满时，Orleans 会取消传递给相关回调的 <xref:System.Threading.CancellationToken>，包括 <xref:Orleans.Grain.OnActivateAsync*>、<xref:Orleans.Grain.OnDeactivateAsync*> 和 Grain 生命周期参与者，然后等待这些代码完成。激活实例会保留 Grain 目录注册及激活实例作用域内的服务，直到这些回调返回。激活超时届满时，激活会在回调返回后失败。停用超时届满时，停用过程会在回调返回后继续。

将令牌传递给状态访问和依赖项调用，使回调在取消后能够及时完成。通常应使这些回调的执行时间远短于超时时长。

激活实例回收器对每一批最多等待一个停用超时时长，然后继续回收其他空闲激活实例。回调仍在运行的激活实例会保留其服务和注册，直至停用完成。

<a id="tune-from-measurements"></a>

## 根据测量结果调优

至少跟踪以下指标：

- 按 Grain 类型划分的激活实例数量及激活/停用速率。
- 状态加载和写入延迟。
- 进程工作集、托管堆大小和分配速率。
- GC 耗时及暂停时长。
- 内存压力削减事件和停用失败。

如果激活实例频繁创建和停用，但内存状态良好，则增加受影响类型的回收时限。如果内存持续居高不下，减少保留的应用程序状态、缩短特定类型的时限、扩容或启用内存压力削减。为 Silo 进程配置[服务器 GC](configuring-garbage-collection.md)。
