---
title: Grain 放置与迁移
description: 了解 Orleans 中的放置机制、默认资源优化策略及激活实例迁移。
ms.date: 08/27/2026
ms.topic: concept-article
---

<a id="grain-placement-and-migration"></a>

# Grain 放置与迁移

当 Grain 未处于活动状态时，Orleans 选择一个兼容的 Silo，并在其中创建激活实例。这个过程称为**放置**（placement）。调用方仍使用具有位置透明性的 Grain 引用，因此放置不会改变应用程序中的调用位置。

本文介绍面向应用程序的放置配置。有关放置及激活实例迁移背后的运行时算法和协调协议，请参阅[放置与激活实例均衡](../implementation/load-balancing.md)。

<a id="what-orleans-balances"></a>

## Orleans 均衡的对象

放置、回收、迁移和负载削减分别解决不同的问题：

| 事件或机制 | Orleans 的处理方式 |
|---|---|
| 调用需要激活实例 | 根据当前兼容的 Silo 及放置统计信息执行放置。 |
| Silo 加入集群 | 成员资格和兼容性信息收敛后，将该 Silo 纳入后续放置决策。现有激活实例继续在当前 Silo 上运行。 |
| 激活实例保持空闲 | 在配置的空闲时间之后回收该实例。后续调用重新执行放置，因此替代激活实例可以利用新增的容量。 |
| Silo 离开或发生故障 | 移除其激活实例。将调用路由到剩余 Silo 上的激活实例，或在这些 Silo 上放置替代激活实例。 |
| 请求显式或自动迁移 | 当前工作完成后迁移符合条件的活动激活实例。集群范围的自动迁移需显式启用。 |
| 已启用的负载削减机制将 Silo 标记为过载 | 客户端网关拒绝请求，流队列流量控制在达到其 CPU 阈值时暂停读取，资源优化放置优先选择未过载的候选 Silo。 |

托管平台或运维人员控制 Silo 数量。Orleans 根据由此形成的集群成员资格调整放置和路由。

<a id="default-placement"></a>

## 默认放置

<xref:Orleans.Runtime.ResourceOptimizedPlacement> 是默认放置策略。它使用采样得到的 Silo 运行时统计信息及 k 选一算法来均衡新增的激活实例。考虑的因素包括 CPU、内存、可用内存、激活实例数量，以及对本地 Silo 的偏好。负载削减机制将 Silo 标记为过载时，放置优先选择未过载的候选 Silo。

有关资源评分和信号平滑的设计背景，请参阅[采用协作式双模卡尔曼滤波的基于资源的放置](https://www.ledjonbehluli.com/posts/orleans_resource_placement_kalman/)。

通过 <xref:Orleans.Configuration.ResourceOptimizedPlacementOptions> 配置其权重：

:::code language="csharp" source="../snippets/compiled/Grains/PlacementSnippets.cs" id="configure_resource_optimized_placement":::
权重是相对值，总和无须为 100。除非测量结果表明特定工作负载需要调整，否则保留默认值。

<a id="scale-out-and-scale-in"></a>

## 横向扩容与缩容

扩容时，新增 Silo 成为新激活实例的候选节点。长期处于活动状态的 Grain 继续在当前 Silo 上运行。Grain 代码或显式启用的再均衡服务可以请求迁移它们。空闲激活实例的回收逐步使更多 Grain 标识可以重新进行放置。

优雅缩容时，离开的 Silo 退出活动成员集合，并在关闭过程中停用其普通激活实例。后续调用会在剩余 Silo 上重新激活这些 Grain 标识。突然失去节点时，故障检测会启用相同的替代放置流程。正在进行的调用可能失败或超时，因此调用方必须遵循应用程序的重试和幂等策略。

逐步扩缩容，并为重新激活、状态读取、缓存预热及暂时集中的流量保留资源余量。参阅[容量规划与扩缩容](../deployment/capacity-planning.md)和[优雅关闭与缩容](../deployment/upgrades.md#graceful-shutdown-and-scale-in)。

<a id="persistence-and-movement"></a>

## 持久化与迁移

持久化 Grain 状态属于 Grain 标识，并跨越激活实例的生命周期保留在配置的存储提供程序中。使用 <xref:Orleans.Runtime.IPersistentState`1> 时，Orleans 在创建普通替代激活实例时，会在 <xref:Orleans.Grain.OnActivateAsync*> 之前读取配置的状态。每个能够承载该 Grain 的 Silo 都必须能够访问配置的存储提供程序。

活动激活实例迁移会将运行时迁移状态直接传输到目标，包括 <xref:Orleans.Runtime.IPersistentState`1> 持有的内存状态。必须在活动迁移后保留的应用程序自有内存状态，必须通过 <xref:Orleans.Runtime.IGrainMigrationParticipant> 参与迁移。等待存储写入完成可提供进程故障后的持久性；迁移状态则提供活动迁移期间的连续性。参阅[Grain 持久化](grain-persistence/index.md)和[激活生命周期与迁移](../implementation/activation-lifecycle.md#activation-migration)。

<a id="load-shedding"></a>

## 负载削减

将 <xref:Orleans.Configuration.LoadSheddingOptions.LoadSheddingEnabled> 设置为 `true` 以启用负载削减。CPU 或内存任一指标超过阈值，都会将 Silo 标记为过载，启用客户端网关请求拒绝，并使资源优化放置优先选择未过载的候选 Silo。使用 `LoadShedQueueFlowController` 的流提供程序根据 CPU 使用率暂停队列读取。

在每个 Silo 上配置它，并根据测量得到的资源余量选择阈值：

:::code language="csharp" source="../snippets/compiled/Grains/PlacementSnippets.cs" id="configure_load_shedding":::

使用负载削减保护请求准入，使用托管平台的自动扩缩容机制管理集群容量，并使用激活实例再均衡或重新分区迁移符合条件的激活实例。将阈值设置在平台硬限制以下，为停用和恢复工作保留余量，并结合 CPU、内存、排队和延迟信号监控拒绝率。

网关负载削减在 CPU 或内存超过其阈值后拒绝传入请求。流队列流量控制根据 CPU 阈值暂停读取。[基于内存的激活实例削减](../host/configuration-guide/activation-collection.md#enable-memory-based-activation-shedding)通过停用选定的激活实例来减少进程内存用量。

<a id="per-grain-strategies"></a>

## 按 Grain 指定策略

当某个 Grain 的要求与默认策略不同时，可以在 Grain 实现上应用放置特性：

| 特性 | 行为 |
|---|---|
| <xref:Orleans.Placement.ResourceOptimizedPlacementAttribute> | 显式选择默认的资源感知策略。 |
| <xref:Orleans.Placement.RandomPlacementAttribute> | 随机选择一个兼容的 Silo。 |
| <xref:Orleans.Placement.PreferLocalPlacementAttribute> | 尽可能使用兼容的本地 Silo。 |
| <xref:Orleans.Placement.HashBasedPlacementAttribute> | 将 Grain ID 映射到当前兼容的 Silo 集合。 |
| <xref:Orleans.Placement.ActivationCountBasedPlacementAttribute> | 优先选择采样中激活实例较少的 Silo。 |
| <xref:Orleans.Placement.SiloRoleBasedPlacementAttribute> | 按 Silo 角色限制放置。 |
| <xref:Orleans.Concurrency.StatelessWorkerAttribute> | 使用本地、可扩展的[工作者池放置](stateless-worker-grains.md)。 |

基于激活实例数量的放置采用[随机负载均衡中的二选一机制](https://www.eecs.harvard.edu/~michaelm/postscripts/mythesis.pdf)一文所述的二选一技术。

:::code language="csharp" source="../snippets/compiled/Grains/PlacementSnippets.cs" id="prefer_local_grain":::
放置发生在创建激活实例时。更改集群成员资格或策略本身不会迁移现有激活实例。

<a id="direct-placement-and-migration-with-placement-hints"></a>

## 使用放置提示直接指定放置和迁移目标

放置提示将新建或正在迁移的激活实例指向特定 Silo。先设置放置提示键 <xref:Orleans.Runtime.Placement.IPlacementDirector.PlacementHintKey>，将其置于 <xref:Orleans.Runtime.RequestContext> 中；此设置应在发出可能触发激活的 Grain 调用或调用 <xref:Orleans.Grain.MigrateOnIdle> 之前完成。应用版本兼容性条件和放置筛选器后，如果提示的 Silo 仍属于兼容候选集合，内置放置决策器便会选择它。

以下 Grain 注入 <xref:Orleans.Runtime.IClusterMembershipService> 和 <xref:Orleans.Runtime.ILocalSiloDetails>，并选择自身之外的一个活动 Silo。`ProcessOrder` 将新的工作者激活实例指向该 Silo。`MoveToAnotherSilo` 在调用 <xref:Orleans.Grain.MigrateOnIdle> 之前于当前请求上下文中设置提示，而 `MoveToAnotherSiloWithExplicitContext` 则将迁移专用字典直接传递给 <xref:Orleans.Runtime.IGrainContext.Migrate*>：

:::code language="csharp" source="snippets/placement/PlacementHints.cs" id="direct_placement_with_hint":::

成员资格快照提供调用方在该时刻已知的 Silo。对于新的激活实例，触发放置的是 Grain 调用，而不是 `GetGrain`。如果工作者已处于活动状态，Orleans 将调用路由到已有激活实例。<xref:Orleans.Grain.MigrateOnIdle> 捕获完整的当前请求上下文。直接调用 <xref:Orleans.Runtime.IGrainContext.Migrate*> 则将所提供的字典用作迁移请求上下文，不改变当前环境中的请求上下文。两种方式都会在当前工作完成后异步启动迁移。只有放置选择了另一个兼容 Silo 时才会发生迁移。

如果成员资格或兼容性变化使提示的 Silo 从候选集合中移除，则配置的放置策略将从当前兼容的 Silo 中选择。使用当前环境中的请求上下文进行传出 Grain 调用或 <xref:Orleans.Grain.MigrateOnIdle> 后，应恢复其先前的值，因为请求上下文会传播到传出的 Grain 调用。

接收方 Grain 也会在其请求上下文中看到放置提示，即使它已经拥有激活实例。在发出无关的传出 Grain 调用之前移除此提示，使这些调用的放置使用各自预期的上下文和策略。如果 Grain 继续处理原始请求，则恢复传入的值：

:::code language="csharp" source="snippets/placement/PlacementHints.cs" id="contain_received_placement_hint":::

<a id="override-the-cluster-default"></a>

## 覆盖集群默认策略

仅当所有未标注放置特性的 Grain 都应使用另一策略时，才注册不同的默认策略：

:::code language="csharp" source="../snippets/compiled/Grains/PlacementSnippets.cs" id="configure_random_placement":::
按 Grain 应用的特性仍然优先。

<a id="placement-filters"></a>

## 放置筛选器

放置筛选器在放置策略选择 Silo 之前缩小兼容候选集合。它们可以基于 [Silo 元数据](../host/configuration-guide/silo-metadata.md)表达要求或偏好。内置元数据筛选特性处于实验阶段，会产生诊断信息 `ORLEANSEXP004`。

有关内置筛选器及其实验状态，请参阅[放置筛选器](grain-placement-filtering.md)。

<a id="request-migration"></a>

## 请求迁移

Grain 可以请求 Orleans 在当前工作完成后迁移其激活实例：

:::code language="csharp" source="../snippets/compiled/Grains/PlacementSnippets.cs" id="move_grain":::
迁移请求具有建议性质，仅当放置选择另一兼容 Silo 时才发生迁移。可使用[放置提示](#direct-placement-and-migration-with-placement-hints)将迁移指向特定兼容 Silo。自定义激活状态必须参与脱水和再水合（dehydration/rehydration）；参阅[Grain 激活与生命周期](grain-lifecycle.md#grain-migration)。

使用 <xref:Orleans.Placement.ImmovableAttribute> 将某个 Grain 类排除在自动迁移之外：

:::code language="csharp" source="../snippets/compiled/Grains/PlacementSnippets.cs" id="immovable_grain":::
此特性不阻止显式调用 <xref:Orleans.Grain.MigrateOnIdle>。

<a id="experimental-automatic-movement"></a>

<a id="experimental-automatic-movement"></a>

<a id="experimental-automatic-movement"></a>

<a id="experimental-automatic-movement"></a>

<a id="automatic-movement"></a>

## 自动迁移

Orleans 包含两种需要显式启用的自动迁移服务：

| 功能 | 目标 |
|---|---|
| 激活实例重新分区器 | 改善 Grain 之间调用的局部性。 |
| 激活实例再均衡器 | 均衡各 Silo 的激活实例数量与内存压力。 |

分别启用这些服务：

:::code language="csharp" source="../snippets/compiled/Grains/PlacementSnippets.cs" id="configure_activation_rebalancing":::
两种功能都迁移符合条件的激活实例，也可以同时运行。它们增加集群协调与状态传输的成本，因此应在生产使用之前对具有代表性的工作负载进行基准测试。无状态工作者、系统目标、Grain 服务、客户端对象以及不可移动的激活实例均不是候选项。

当激活实例数量或激活实例所占内存分布不均时，选择激活实例再均衡器。当 Grain 之间的跨 Silo 调用是问题时，选择激活实例重新分区器。同时启用两者，可使重新分区器的默认容差规则纳入再均衡器对集群不均衡程度的判断。配合托管平台自动扩缩容机制管理容量，以及负载削减控制过载时的请求准入。有关调优和可观测性详情，请参阅[放置与激活实例均衡](../implementation/load-balancing.md#choosing-the-mechanism)。

<a id="implement-custom-placement"></a>

## 实现自定义放置

自定义放置策略和决策器是高级运行时扩展。只有内置策略结合[放置筛选器](grain-placement-filtering.md)无法表达需求时，才实现它们。自定义实现包含三部分：

1. 用于标识策略的 <xref:Orleans.Runtime.PlacementStrategy>。
1. 将策略应用到 Grain 类的 <xref:Orleans.Placement.PlacementAttribute>。
1. 选择一个候选 Silo 的 <xref:Orleans.Runtime.Placement.IPlacementDirector>。

以下示例让使用相同 Grain 键的相关 Grain 类型具有对同一 Silo 的亲和性。这不同于 <xref:Orleans.Runtime.HashBasedPlacement>，后者对完整 Grain ID（包括 Grain 类型）进行哈希。

首先定义策略及其特性：

:::code language="csharp" source="snippets/placement/CustomPlacement.cs" id="custom_placement_strategy":::

放置策略实例可能跨越运行时序列化边界。使用 `[GenerateSerializer]`；如果策略增加可序列化状态，为其成员分配稳定的 `[Id(n)]` 值。本例中的策略不可变，没有需要序列化的成员。

接下来实现决策器：

:::code language="csharp" source="snippets/placement/CustomPlacement.cs" id="custom_placement_director":::

调用 <xref:Orleans.Runtime.Placement.IPlacementContext.GetCompatibleSilos*>，而不是自行重建集群成员资格。它在放置筛选器运行之后返回可以承载该 Grain 类型且满足接口版本兼容性的活动 Silo。如果这个过程未留下候选 Silo，当前运行时会抛出异常；显式检查空集合也可以保护测试或其他上下文实现中的取模操作。<xref:Orleans.Runtime.Placement.IPlacementDirector.GetPlacementHint*> 仅在请求提示指向其中一个候选 Silo 时接受该提示，因此示例先遵循有效提示，再应用自己的策略。

决策器在按索引选择之前先对候选地址排序，并使用 Grain 键稳定且均匀的哈希。因此，两个具有相同键的 Grain 类型仅在看到相同候选集合时才选择同一个 Silo：

:::code language="csharp" source="snippets/placement/CustomPlacement.cs" id="apply_custom_placement":::

这是亲和性，而不是持久的固定绑定。成员资格变化、Silo 重启或不同的兼容性和筛选结果都可能改变映射。现有激活实例不会仅因为后续的放置决策映射到了其他位置就迁移。均匀哈希在各集群节点之间具有确定性，但不是加密哈希，因此不要将放置用作授权或隔离边界。

最后，在每个 Silo 上注册策略和决策器：

:::code language="csharp" source="snippets/placement/CustomPlacement.cs" id="register_custom_placement":::

此重载将无状态策略和决策器注册为键控单例。其他重载可以更改策略生命周期，但决策器仍是键控单例。因此，决策器必须是线程安全的，并使用适合单例的依赖项。如果策略携带特性配置，请通过 <xref:Orleans.Runtime.PlacementStrategy.PopulateGrainProperties*> 和 <xref:Orleans.Runtime.PlacementStrategy.Initialize*> 保留这些配置，并使用不会在不同 Grain 类型之间共享可变配置的生命周期。

本示例有意以资源感知均衡换取亲和性。如果负载是主要关注点，优先使用 <xref:Orleans.Runtime.ResourceOptimizedPlacement>，或应用筛选器，让内置放置策略从剩余候选项中进行选择。有关更多实现，请查看 [`src/Orleans.Runtime/Placement`](https://github.com/dotnet/orleans/tree/main/src/Orleans.Runtime/Placement) 下的内置决策器，包括 [`HashBasedPlacementDirector`](https://github.com/dotnet/orleans/blob/main/src/Orleans.Runtime/Placement/HashBasedPlacementDirector.cs)。
