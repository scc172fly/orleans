---
title: "Grain 安置和移徙"
description: "理解位置、资源优化违约和 Orleans的启动动作。"
ms.date: 08/27/2026
ms.topic: concept-article
---

<a id="grain-placement-and-migration"></a>

# Grain 安置和移徙

当 grain 不活动时, Orleans 选择兼容的 silo 并在那里创建激活。 此进程是 **位置**。 呼叫者继续使用不透明的位置 grain 引用, 因此定位不会改变应用程序调用站点 。

本条涵盖应用程序应用定位配置。关于放置和激活移动背后的运行时算法和协调协议,见 [安置和启动平衡](../implementation/load-balancing.md)。

<a id="what-orleans-balances"></a>

## 多少 Orleans 余额

安置、收集、移徙和负荷消散解决了各种问题:

| 事件或机制 | Orleans 做什么 |
|---|---|
| 需要启动一个呼叫 | 使用当前兼容 silos 和当前配置统计运行位置。 |
| A silo 加入 | 将它包括在成员构成和兼容性信息汇集后以后的安置决定中。 silos. |
| 启动未动 | 配置的空闲期后收集它。 稍后调用再次运行位置, 这样替换激活可以使用新添加的能力 。 |
| A silo 离开或失败 | 移除其激活。 电话被发送到剩余 silos 的激活或导致替换启动被放置在那里。 |
| 要求进行明确或自动移徙 | 在当前工作完成后,可移动符合资格的活动。全集群自动迁移是选择加入。 |
| 启用的负载切换标记 a silo 超载 | 客户网关拒绝申请,流程控制暂停在CPU门槛处出现,资源优化安置有利于未超载的候选人。 |

主机平台或操作员控制 silo 计数。 Orleans 将位置和路线适应所导致成员。

<a id="default-placement"></a>

## 默认配置

<xref:Orleans.Runtime.ResourceOptimizedPlacement> is the default placement strategy. It uses sampled silo runtime statistics and a power-of-k-choices algorithm to balance new activations. It considers CPU, memory, available memory, activation count, and a preference for the local silo. When load shedding marks silos overloaded, placement favors non-overloaded candidates.

有关其资源评分和信号平滑的设计背景,见 [以资源为基础的安置,同时采用合作双模两式Kalman过滤过滤器](https://www.ledjonbehluli.com/posts/orleans_resource_placement_kalman/)。

配置其通过 <xref:Orleans.Configuration.ResourceOptimizedPlacementOptions>的重量 :

:::code language="csharp" source="../snippets/compiled/Grains/PlacementSnippets.cs" id="configure_resource_optimized_placement":::
重量相对比较,不需要总计100。 在测量显示有特定工作量原因改变这些系数之前, 继续违约。

<a id="scale-out-and-scale-in"></a>

## 缩放和缩放

在扩大时,新的 silos 成为新启动的候选。长期有效 grains 继续运行当前 silos。 Grain 代码或选择适用的重新平衡服务可以要求迁移。闲置激活收集逐渐使更多的 grain 身份有资格获得新的安置。

宽厚的称号,至一离的, silo 停止营业期间,会后电话恢复正常启动程序 grain 剩余身份 silos。在突然丢失后,检测失败可以选择相同的替换定位路径。飞行中电话会失败或暂停,因此呼叫者必须遵循应用程序的再测试和应急政策。

逐渐缩放并保留主机房,以便重新激活、读取、缓存温度升高和临时集中交通。 [能力规划和规模](../deployment/capacity-planning.md) 和 [优优异关闭和规模化](../deployment/upgrades.md#graceful-shutdown-and-scale-in).

<a id="persistence-and-movement"></a>

## 持久性和运动

持久性 grain 国家属于 grain 在整个激活寿命期内,在配置的存储提供者中保留身份和身份。 <xref:Orleans.Runtime.IPersistentState`1>, Orleans 之前读过配置状态 <xref:Orleans.Grain.OnActivateAsync*> 当它创造普通的替换激活时。 silo 能够作为 grain 必须能够到达配置的存储提供者。

实时启动移民转移活动运行时,移徙状态直接与目标相关,包括 <xref:Orleans.Runtime.IPersistentState`1>所保持的在役状态。必须生存在一次流动中的申请拥有的在役状态必须通过 <xref:Orleans.Runtime.IGrainMigrationParticipant>参与。等待存储书写道,整个过程失败具有持久性;移徙状态为一次流动提供连续性。见 [Grain 持久性](grain-persistence/index.md) 和 [激活生命周期和移徙](../implementation/activation-lifecycle.md#activation-migration)。

<a id="load-shedding"></a>

## 装入套装

设置 <xref:Orleans.Configuration.LoadSheddingOptions.LoadSheddingEnabled> 到 `true` 以激活负载切换。 跨越 CPU 或存储阈值将 silo 标记为超载, 允许客户端- 门户请求被拒绝, 并让资源优化的放置有利于未加载的候选人。 使用 `LoadShedQueueFlowController` 暂停队列的流源供应商会按照 CPU 的用法读取 。

配置在每一个 silo 上, 从测量的总机中选择阈值 :

:::code language="csharp" source="../snippets/compiled/Grains/PlacementSnippets.cs" id="configure_load_shedding":::

使用负荷缓存保护, 集束容量的主机平台自动标尺, 启动重新平衡或再分配以适应符合激活条件的动作。 设定平台硬限以下的阈值,保留停用和回收工作的总机房,并监控CPU、内存、排队和潜伏信号的拒绝率。

网关负荷在 CPU 或 内存 过界后拒绝收到的请求。 流队列流程控制使用 CPU 阈值来暂停读取 。 [基于记忆的激活停止](../host/configuration-guide/activation-collection.md#enable-memory-based-activation-shedding) 禁用选中的激活可以减少进程内存 。

<a id="per-grain-strategies"></a>

## /grain 战略

当 grain 执行的要求不同于默认时,应用其配置属性来实施:

| 属性属性 | 行为行为 |
|---|---|
| <xref:Orleans.Placement.ResourceOptimizedPlacementAttribute> | 明确选择默认的资源意识战略。 |
| <xref:Orleans.Placement.RandomPlacementAttribute> | 选择随机兼容的 silo。 |
| <xref:Orleans.Placement.PreferLocalPlacementAttribute> | 在可能的情况下使用本地兼容 silo 。 |
| <xref:Orleans.Placement.HashBasedPlacementAttribute> | 地图 grain 当前兼容的跨当前 ID silo 设置。 |
| <xref:Orleans.Placement.ActivationCountBasedPlacementAttribute> | 福弗斯抽样 silos ,但启动量较少。 |
| <xref:Orleans.Placement.SiloRoleBasedPlacementAttribute> | 限制 silo 角色的安置。 |
| <xref:Orleans.Concurrency.StatelessWorkerAttribute> | 使用本地可缩放 [工人-集体安置](stateless-worker-grains.md)。 |

激活-按点计的安置应用了以下所述二选一办法的两权能力技术: [随机化载负平衡中两种选择的力量](https://www.eecs.harvard.edu/~michaelm/postscripts/mythesis.pdf).

:::code language="csharp" source="../snippets/compiled/Grains/PlacementSnippets.cs" id="prefer_local_grain":::
创建激活时会发生位置定位。 改变集群成员资格或策略不会自动移动现有的激活 。

<a id="direct-placement-and-migration-with-placement-hints"></a>

## 直接安置和有安置提示的移徙

定位提示将新激活或迁移启动指向特定 silo设置 <xref:Orleans.Runtime.Placement.IPlacementDirector.PlacementHintKey> 内 <xref:Orleans.Runtime.RequestContext> 在下 grain 触发激活或拨打前 <xref:Orleans.Grain.MigrateOnIdle>。内置安置主任选择提示 silo 当它属于在应用版本兼容性和放置过滤器后设定的兼容性候选人时。

下列 grain 输入 <xref:Orleans.Runtime.IClusterMembershipService> 和 <xref:Orleans.Runtime.ILocalSiloDetails> ,并选择了除其自身以外的活动 silo 。 `ProcessOrder` 将新工人的激活指向到该 silo。 `MoveToAnotherSilo` 在调用 <xref:Orleans.Grain.MigrateOnIdle>之前在当前请求背景下设置提示,而 `MoveToAnotherSiloWithExplicitContext` 直接通过一个针对移民的字典到 <xref:Orleans.Runtime.IGrainContext.Migrate*>:

:::code language="csharp" source="snippets/placement/PlacementHints.cs" id="direct_placement_with_hint":::

成员简略介绍 silos 此时点已知的呼叫器知道。 对于新激活, grain 而不是 `GetGrain`如果工人已经投入了工作 Orleans 将呼叫引向其现有激活。 <xref:Orleans.Grain.MigrateOnIdle> 捕捉完整当前请求的上下文。 <xref:Orleans.Runtime.IGrainContext.Migrate*> 两种表格在当前工作完成后开始不同步地进行迁移。只有在安置选择不同的兼容时才会进行迁徙 silo.

如果会籍或兼容性改变后,从候选人集中删除了提示 silo ,则配置的安插策略从当前兼容 silos中选择。在使用环境请求上下文来调出 grain 呼叫或 <xref:Orleans.Grain.MigrateOnIdle>后,恢复其先前值,因为请求上下文向发送 grain 呼叫时会传播请求上下文。

接收方 grain 还在其请求中看到定位提示,包括已经激活的。在发出不相关的发送 grain 电话之前删除提示,以便其配置使用预定的背景和战略,然后恢复收到值,如果 grain 继续处理原始请求:

:::code language="csharp" source="snippets/placement/PlacementHints.cs" id="contain_received_placement_hint":::

<a id="override-the-cluster-default"></a>

## 覆盖组群默认值

仅在所有未附加说明的 grains 都应使用时, 登记不同的默认策略:

:::code language="csharp" source="../snippets/compiled/Grains/PlacementSnippets.cs" id="configure_random_placement":::
每grain 属性仍然优先。

<a id="placement-filters"></a>

## 放置过滤器

在职位安排战略选择a之前, 插入过滤器会减少兼容的候选人设置 silo它们可以基于以下原则表达要求或偏重: [silo 元元数据](../host/configuration-guide/silo-metadata.md)内置元数据过滤器属性是实验性的,产生诊断 `ORLEANSEXP004`.

内置过滤器和实验状态见 [放置过滤器](grain-placement-filtering.md) 。

<a id="request-migration"></a>

## 要求移徙

A grain 在当前工作完成后可以要求 Orleans 移动启动:

:::code language="csharp" source="../snippets/compiled/Grains/PlacementSnippets.cs" id="move_grain":::
移徙是咨询性的,只有在安置选择了另一个兼容 silo的情况下才能进行。使用 [职位布插提示](#direct-placement-and-migration-with-placement-hints) 将移民引向一个特定的兼容 silo。自定义启动国必须参与脱水和补液;见 [Grain 激活和生命周期](grain-lifecycle.md#grain-migration)。

使用 <xref:Orleans.Placement.ImmovableAttribute> 将一个 grain 类排除在自动移动之外:

:::code language="csharp" source="../snippets/compiled/Grains/PlacementSnippets.cs" id="immovable_grain":::
属性不会阻止直线呼叫 <xref:Orleans.Grain.MigrateOnIdle> 。

<a id="experimental-automatic-movement"></a>

<a id="automatic-movement"></a>

## 自动移动

Orleans 包括两个自动移动的选入服务:

| 特征 | 目标目标目标目标目标目标目标 |
|---|---|
| 重新开发 | 改进 grain- 到grain 调用地点。 |
| 激活再平衡器 | 整个 silos的平衡激活计数和内存压力。 |

独立启用服务 :

:::code language="csharp" source="../snippets/compiled/Grains/PlacementSnippets.cs" id="configure_activation_rebalancing":::
这两种特征都具有迁移合格激活并可以同时运作的特点。它们增加了集群协调和国家转移成本,因此在生产使用前就以代表性工作量为基准。 无国籍工人、系统目标、 grain 服务、客户对象和不动产启动都不是候选者。

当问题在于激活的计算或激活记忆时, 选择激活的重新平衡。 当交叉时选择激活的重新分配silo 之间通话 grains 问题所在。 启用这两个功能, 让重新分配者的默认容忍规则包含再平衡者对集群不平衡的看法。 用主机平台自动标尺来匹配它们容量和超载接收控制负荷的卸载。 见 [安置和启动平衡](../implementation/load-balancing.md#choosing-the-mechanism) 用于调整和观察细节。

<a id="implement-custom-placement"></a>

## 实施自定义安放

自定义职位安排策略和董事是高级运行时间延长。 只有当内置战略加上 [放置过滤器](grain-placement-filtering.md) 无法表达要求时才执行这些策略。 定制实施有三个部分:

1. 确定政策的一个 <xref:Orleans.Runtime.PlacementStrategy> 。
1. A <xref:Orleans.Placement.PlacementAttribute> 将政策适用于 grain 类.
1. A 音 A <xref:Orleans.Runtime.Placement.IPlacementDirector> 选择一个候选人 silo.

以下示例说明 grain 相同类型的亲和关系 silo 当它们使用相同的 grain 键,此选项与 <xref:Orleans.Runtime.HashBasedPlacement>,它杂置了完整 grain ID, 包括 grain 类型。

首先,界定战略及其属性:

:::code language="csharp" source="snippets/placement/CustomPlacement.cs" id="custom_placement_strategy":::

设置策略实例可以跨运行时间序列边界。使用 `[GenerateSerializer]`; 如果该策略添加了可连序状态, 请给其成员指定稳定的 `[Id(n)]` 值。 本示例中的策略是不可改变的, 没有连序成员 。

执行主任:

:::code language="csharp" source="snippets/placement/CustomPlacement.cs" id="custom_placement_director":::

调调 <xref:Orleans.Runtime.Placement.IPlacementContext.GetCompatibleSilos*> 而不是重建分组成员。 它返回活动 silos 能够作为 grain 输入类型, 并在设置过滤器运行后满足界面转换兼容性。 如果当前运行时该程序没有候选人被丢弃, 则会扔出当前运行时间; 明确的空位检查还可以在测试或其它环境执行中保护模版操作 。 <xref:Orleans.Runtime.Placement.IPlacementDirector.GetPlacementHint*> 仅在提名其中一名候选人时才接受请求提示, 因此示例在应用自己的政策之前, 尊重有效的提示 。

主任在对候选人地址进行索引前先排序其地址,然后使用稳定、统一的 grain 键的散列。因此,两个具有相同键的 grain 类型只有在他们看到相同的候选人组时才能选择相同的 silo :

:::code language="csharp" source="snippets/placement/CustomPlacement.cs" id="apply_custom_placement":::

这是近似关系,不是持久的钉钉。 成员变更、 silo 重新启动或不同的兼容性和过滤结果可以改变映射。 现有的激活不会仅仅因为稍后在别处放置决定地图而移动。 统一的散列是跨集节点的确定性,但不是加密,所以不要将定位用作授权或隔离边界。

最后,对战略进行登记,并在每个 silo:

:::code language="csharp" source="snippets/placement/CustomPlacement.cs" id="register_custom_placement":::

这种超载将无国籍战略和董事作为关键单吨。其他超载可以改变策略的寿命,但主任仍是一个关键单吨。因此,董事必须使用安全线并使用单一吨安全的依赖性。如果战略带有属性配置,则通过 <xref:Orleans.Runtime.PlacementStrategy.PopulateGrainProperties*> 和 <xref:Orleans.Runtime.PlacementStrategy.Initialize*> 来保存,并且使用一个在 grain 类型之间不具有可变组合的寿命。

这个例子故意将资源意识平衡换成近亲关系。 如果负载是首要关切,则更喜欢 <xref:Orleans.Runtime.ResourceOptimizedPlacement> 或应用过滤器,让内置安置战略从其余候选人中作出选择。对于更多的执行,要检查 [`src/Orleans.Runtime/Placement`](https://github.com/dotnet/orleans/tree/main/src/Orleans.Runtime/Placement)下的内设主任,包括 [`HashBasedPlacementDirector`](https://github.com/dotnet/orleans/blob/main/src/Orleans.Runtime/Placement/HashBasedPlacementDirector.cs)。
