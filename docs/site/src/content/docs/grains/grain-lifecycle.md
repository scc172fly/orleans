---
title: Grain 激活与生命周期
description: 了解 Orleans 中 Grain 的激活、停用、回收、生命周期参与和迁移。
ms.date: 08/07/2026
ms.topic: concept-article
---

<a id="grain-activation-and-lifecycle"></a>

# Grain 激活与生命周期

Orleans 按需激活 Grain，并停用空闲的激活实例以回收资源。激活是 Grain 稳定逻辑标识背后的实现细节：即使激活实例发生变化，调用方仍可使用同一个 Grain 引用。

<a id="activation"></a>

## 激活

Orleans 通过依赖注入创建 Grain 类的实例，建立 Grain 上下文，加载已配置的持久化状态，然后调用 <xref:Orleans.Grain.OnActivateAsync*>。

重写带取消令牌的重载：

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="activate_grain":::

<xref:Orleans.Grain.OnActivateAsync*> 接受 <xref:System.Threading.CancellationToken>，没有无参重载。如果激活失败，Orleans 不会让该激活实例接受调用。

避免在激活期间执行不必要的工作。激活实例可能在回收、迁移、Silo 重启或故障之后重新创建。

<a id="deactivation"></a>

## 停用

如果激活实例处于空闲状态、Silo 正在停止、应用请求停用、正在迁移，或者发生使激活实例无效的错误，Orleans 可以停用该实例。

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="deactivate_grain":::

停用是尽力而为的操作。进程突然终止或发生某些故障时，<xref:Orleans.Grain.OnDeactivateAsync*> 不会执行。应在修改重要状态的操作中进行持久化，而不要仅依靠停用阶段。

<a id="influence-activation-lifetime"></a>

## 影响激活实例的生命周期

调用 <xref:Orleans.Grain.DeactivateOnIdle>，请求 Orleans 在当前请求与排队的工作完成后停用 Grain：

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="deactivate_on_idle":::

调用 <xref:Orleans.Grain.DelayDeactivation*>，使本来空闲的激活实例在指定时间内仍可继续使用。这只是一个提示，不是持久性保证；故障与关闭仍可能移除该激活实例。

默认情况下，Grain 定时器不会延长激活实例的生存期。仅当定时器活动需要延长生存期时，才设置 <xref:Orleans.Runtime.GrainTimerCreationOptions.KeepAlive?displayProperty=nameWithType>。

<a id="lifecycle-stages-and-participants"></a>

## 生命周期阶段和参与者

Grain 生命周期提供有序的阶段：

| 阶段 | 用途 |
|---|---|
| <xref:Orleans.Runtime.GrainLifecycleStage.First?displayProperty=nameWithType> | 最早的订阅阶段。 |
| <xref:Orleans.Runtime.GrainLifecycleStage.SetupState?displayProperty=nameWithType> | 状态初始化与加载。 |
| <xref:Orleans.Runtime.GrainLifecycleStage.Activate?displayProperty=nameWithType> | Grain 激活与停用回调。 |
| <xref:Orleans.Runtime.GrainLifecycleStage.Last?displayProperty=nameWithType> | 最晚的订阅阶段。 |

需要按顺序执行激活实例级行为的组件，可以为 <xref:Orleans.Runtime.IGrainLifecycle> 实现 <xref:Orleans.ILifecycleParticipant`1>，并通过 <xref:Orleans.Runtime.IGrainContext.ObservableLifecycle> 订阅生命周期。当一个组件的启动依赖另一个组件完成启动时，应使用不同的阶段。同一阶段内的回调可以并发执行。

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="lifecycle_participant":::

运行时会对实现参与者接口的 Grain 对象调用 `Participate`。服务需要明确的注册责任方，例如 facet 工厂或下文所述的共享激活初始化机制。

<a id="shared-activation-setup"></a>

### 共享激活初始化

使用 <xref:Orleans.Runtime.IConfigureGrainTypeComponents> 为 Grain 实现类选择功能，并使用 <xref:Orleans.Runtime.GrainTypeSharedContext.AddActivationSetup*> 注册可复用的初始化操作。此示例选择实现了应用自定义标记接口 `ICachedGrain` 的类：

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="activation_setup":::

将配置器注册为单例，并将功能状态注册为作用域服务：

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="activation_setup_registration":::

Orleans 将选定的初始化操作缓存在共享 Grain 类型上下文中。每个激活实例在 Grain 构造函数完成、<xref:Orleans.Runtime.IGrainContext.GrainInstance> 已赋值后，按照注册顺序执行这些操作。所有初始化操作完成后，运行时才调用 Grain 对象的 `Participate` 方法并开始生命周期回调。无状态工作者 Grain 的每个激活实例都使用自己的上下文执行同一组共享初始化操作。

共享操作仅为选定的 Grain 解析 `CacheParticipant`。解析使用激活实例的作用域，因此构造函数注入的 `CacheParticipant` 与初始化操作共享同一个作用域服务实例。如果通过接口注入，请注册一个解析该具体服务的别名工厂。普通的具体类型、键控服务及参与者接口的 DI 注册仍保留各自显式注册生命周期参与者的行为。

不同激活实例的初始化操作可以并发执行。共享操作应保持无状态，或者确保其捕获的共享数据可安全并发访问；将激活实例专属状态保存在激活作用域中。在共享类型配置期间添加操作。通过同步初始化注册服务，并通过生命周期回调完成异步初始化与关闭。每个功能应指定一个注册责任方，以确保只订阅一次。

初始化异常会使激活失败，跳过剩余的初始化操作与生命周期启动，并触发 Grain 和激活作用域的释放。新的激活实例会解析新的作用域状态，再次执行缓存的初始化操作。

关于 Silo 与 Grain 激活实例共享的运行时生命周期模型，请参阅 [Orleans 运行时生命周期](../implementation/orleans-lifecycle.md)。

<a id="grain-migration"></a>

## Grain 迁移

迁移将激活实例移动到另一个 Silo，同时保留参与迁移的内存状态。调用 <xref:Orleans.Grain.MigrateOnIdle>，请求在激活实例完成当前工作之后迁移：

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="migrate_on_idle":::

此请求是建议性的。只有放置决策选中了另一个兼容 Silo，才会发生迁移。Orleans 会将当前 <xref:Orleans.Runtime.RequestContext> 传递给放置决策过程。

对于必须在迁移过程中保留的自定义激活实例状态，请实现 <xref:Orleans.Runtime.IGrainMigrationParticipant>：

:::code language="csharp" source="../snippets/compiled/Grains/GrainSnippets.cs" id="migration_participant":::

Orleans 提供的持久化状态组件会自动参与迁移。迁移不能替代持久化存储：如果源进程在传输完成前发生故障，待迁移的状态仍会丢失。

自动激活实例重分区与再均衡会利用迁移来改善局部性或集群负载均衡。有关配置，请参阅 [Grain 放置](grain-placement.md)。

使用 <xref:Orleans.Placement.ImmovableAttribute> 将某个 Grain 类型排除在自动迁移之外。它不会阻止显式的 <xref:Orleans.Grain.MigrateOnIdle> 请求。

关于激活、回收、停用与迁移背后的运行时协议，请参阅 [激活实例生命周期与迁移](../implementation/activation-lifecycle.md)。应用代码应继续依赖本文介绍的公共生命周期 API，而不是运行时内部实现。
