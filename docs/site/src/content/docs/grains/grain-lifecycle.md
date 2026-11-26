---
title: "Grain 激活和生命周期"
description: "理解 grain 激活、停止使用、收集、生命周期参与和迁移 Orleans。"
ms.date: 08/07/2026
ms.topic: concept-article
---

<a id="grain-activation-and-lifecycle"></a>

# Grain 激活和生命周期

Orleans 应要求启动 grains ,并停止闲置激活以回收资源。启动是 grain稳定逻辑身份的实施细节:在激活变化中,调用者继续使用相同的 grain 参考值。

<a id="activation"></a>

## 激活

Orleans 通过自给性注射创建 grain 类, 建立其 grain 上下文, 负载配置的持久性状态, 然后调用 <xref:Orleans.Grain.OnActivateAsync*>。

覆盖取消的当量超载 :

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="activate_grain":::

<xref:Orleans.Grain.OnActivateAsync*> 接受一个 <xref:System.Threading.CancellationToken>;没有无参数超载。如果激活失败, Orleans 不提供该启动用于呼叫。

避免在激活期间做不必要的工作。 在收集、迁移、 silo 重新启动或失败后,可以重新恢复活动。

<a id="deactivation"></a>

## 解除活动

Orleans 能够解除激活,因为它已经闲置、 silo 停止、申请请求的禁用、迁移发生或错误导致启动无效。

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="deactivate_grain":::

关闭是最好的努力。 <xref:Orleans.Grain.OnDeactivateAsync*> 如果进程突然终止或有些失败, 则不会运行。 作为改变它的行动的一部分, 不仅在停止操作期间, 并且作为该操作的一部分存在重要状态 。

<a id="influence-activation-lifetime"></a>

## 影响启动寿命

在当前请求和排队工作完成后,调用 <xref:Orleans.Grain.DeactivateOnIdle> 询问 Orleans 停止 grain :

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="deactivate_on_idle":::

呼叫 <xref:Orleans.Grain.DelayDeactivation*> , 以保持一个其他闲置激活在特定期限内的合格状态。 这是提示, 不是持久性保证; 故障和关闭仍然可以取消激活 。

Grain 计时器无法默认保持激活活性。 仅在定时器活动延长激活寿命时才设定 <xref:Orleans.Runtime.GrainTimerCreationOptions.KeepAlive?displayProperty=nameWithType> 。

<a id="lifecycle-stages-and-participants"></a>

## 生命周期阶段和参与者

grain 生命周期暴露了定序阶段:

| 阶段 | 目的目的宗旨 |
|---|---|
| <xref:Orleans.Runtime.GrainLifecycleStage.First?displayProperty=nameWithType> | 最迫切的订阅点 |
| <xref:Orleans.Runtime.GrainLifecycleStage.SetupState?displayProperty=nameWithType> | 国家设置和装货。 |
| <xref:Orleans.Runtime.GrainLifecycleStage.Activate?displayProperty=nameWithType> | Grain 启动和解除回击 |
| <xref:Orleans.Runtime.GrainLifecycleStage.Last?displayProperty=nameWithType> | 最新订阅点。 |

需要有定序启动波及行为 <xref:Orleans.ILifecycleParticipant`1> 用于 <xref:Orleans.Runtime.IGrainLifecycle> 订阅 <xref:Orleans.Runtime.IGrainContext.ObservableLifecycle>。当一个部件的启动取决于另一个部件完成时,使用不同的阶段。在某一阶段内进行回调可以同时执行。

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="lifecycle_participant":::

运行时间调用 `Participate` 上a grain 服务使用明确的注册所有人,例如面板工厂或下文所述的共享启动装置。

<a id="shared-activation-setup"></a>

### 共享激活集共享设置

使用 <xref:Orleans.Runtime.IConfigureGrainTypeComponents> 选择一个 grain 执行类别的特点,并用 <xref:Orleans.Runtime.GrainTypeSharedContext.AddActivationSetup*>登记可重复使用的设置行动。本示例选择实施应用程序自有 `ICachedGrain` 标记的分类:

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="activation_setup":::

将配置登记为单吨,并将特征状态登记为范围:

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="activation_setup_registration":::

Orleans 缓存共享 grain 类型中选定的设置动作。 每次激活都在 grain 构建器完成和指定 <xref:Orleans.Runtime.IGrainContext.GrainInstance> 后按登记顺序运行这些动作。所有设置动作在运行时间拨打 grain 对象的 `Participate` 方法之前就结束,并开始生命周期回调。每个无国籍工人启动时都使用与其自身背景相同的共享设置。

共同行动决心 `CacheParticipant` 仅为选中 grains分辨率使用激活范围,因此制造器注入 `CacheParticipant` 并设置相同的服务范围。 对于界面注射,注册一个别名工厂解决具体服务问题。普通混凝土、密钥和参与者界面识别码登记保留了明确的入学行为。

设置动作可以同时运行不同激活。 保持共享行动无国籍状态, 或者让捕获的共享数据安全地同时访问; 保持启动范围中特定激活状态; 在共享类型配置中添加动作 。 使用同步设置来注册服务和生命周期回调不同步初始化和关闭。 给每个特性指派一个注册所有人, 以便一次建立订阅 。

设置例外失败了激活, 跳过剩余设置动作和生命周期启动, 触发 grain 和激活范围处置。 新的激活可以解决新扩展状态并再次运行缓存设置 。

关于 silos 和 grain 激活所共有的运行时生命周期模型,见 [Orleans 运行时间生命周期](../implementation/orleans-lifecycle.md)。

<a id="grain-migration"></a>

## Grain 移徙

移徙会将一个活动转移到另一个 silo 保留移民参与的模拟状态。 <xref:Orleans.Grain.MigrateOnIdle> 激活完成当前工作后请求迁移:

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="migrate_on_idle":::

只有在安置选择另一个兼容的 silo时,才进行移民。 Orleans 将目前的 <xref:Orleans.Runtime.RequestContext> 纳入安置决定。

执行必须生存下来的自定义活化状态 <xref:Orleans.Runtime.IGrainMigrationParticipant> :

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="migration_participant":::

由 Orleans 提供的持久性状态组件自动参与。移民不能替代耐久储存:如果源程序在传输完成之前失败,迁移状态仍然会消失。

自动激活重新分配和再平衡利用迁移来改善地点或组群的平衡。 [Grain 安置](grain-placement.md) 用于配置。

使用 <xref:Orleans.Placement.ImmovableAttribute> 将一个 grain 类型排除在自动迁移之外。 它不阻拦明确的 <xref:Orleans.Grain.MigrateOnIdle> 请求 。

启动、收集、禁用和迁移后运行时间协议,请参见 [激活生命周期和移徙](../implementation/activation-lifecycle.md)。应用代码应继续依赖此处描述的公共生命周期API,而不是运行时内部。
