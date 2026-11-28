---
title: Grain 放置筛选器
description: 使用 Silo 元数据筛选 Orleans Grain 放置的候选 Silo。
ms.date: 08/08/2026
ms.topic: concept-article
---

<a id="grain-placement-filters"></a>
# Grain 放置筛选器

> [!WARNING]
> 内置的 <xref:Orleans.Runtime.Placement.Filtering.RequiredMatchSiloMetadataPlacementFilterAttribute> 和 <xref:Orleans.Runtime.Placement.Filtering.PreferredMatchSiloMetadataPlacementFilterAttribute> 属于实验性功能，标注了 `Experimental("ORLEANSEXP004")`。此诊断不适用于下文所述的自定义筛选器 API。

放置机制首先确定与 Grain 类型兼容的 Silo。随后，筛选器按顺序缩小候选集合，最后由 Grain 的[放置策略](grain-placement.md)选出目标。

使用筛选器表达可用区、硬件能力或部署层级等约束和偏好。不要用它们编码每个请求的业务路由，从而使同一 Grain 标识表现得像多个独立实体。

<a id="configure-silo-metadata"></a>
## 配置 Silo 元数据

内置筛选器将发起调用的 Silo 的 [Silo 元数据](../host/configuration-guide/silo-metadata.md)与候选 Silo 的元数据进行比较。请在所有参与的 Silo 上配置一致的键和值：

:::code language="csharp" source="../snippets/compiled/Grains/PlacementSnippets.cs" id="configure_silo_metadata":::
外部客户端没有 Silo 元数据。设计使用筛选器的 Grain 激活路径时，应确保发起放置请求的是具有所需元数据的 Silo。

<a id="require-metadata-matches"></a>
## 要求元数据匹配

<xref:Orleans.Runtime.Placement.Filtering.RequiredMatchSiloMetadataPlacementFilterAttribute> 仅保留所有配置键都匹配的候选 Silo：

:::code language="csharp" source="../snippets/compiled/Grains/PlacementSnippets.cs" id="required_metadata_filter":::
如果没有兼容的 Silo 匹配，则放置失败。仅在存在硬性要求时使用此筛选器。

<a id="prefer-metadata-matches"></a>
## 优先选择元数据匹配的 Silo

<xref:Orleans.Runtime.Placement.Filtering.PreferredMatchSiloMetadataPlacementFilterAttribute> 优先选择匹配有序键列表的候选 Silo，并通过逐步去掉列表中较靠前的键来放宽匹配条件：

:::code language="csharp" source="../snippets/compiled/Grains/PlacementSnippets.cs" id="preferred_metadata_filter":::
`minCandidates` 防止过于严格的偏好导致放置集中在少数 Silo 上。其默认值为 2。

<a id="combine-and-order-filters"></a>
## 组合筛选器并指定顺序

Grain 类存在多个筛选器时，应为每个筛选器指定唯一的 `order` 值。Orleans 先应用值较小的筛选器。特性的声明顺序并不是可靠的排序依据。

:::code language="csharp" source="../snippets/compiled/Grains/PlacementSnippets.cs" id="ordered_metadata_filters":::
放置策略只能看到经过所有筛选器筛选后保留的候选 Silo。偏好筛选器只能在接收到的候选集合内扩大匹配范围，不能恢复此前被硬性要求筛选器移除的候选 Silo。

<a id="read-metadata-from-a-grain"></a>
## 从 Grain 中读取元数据

当 Grain 逻辑需要 Silo 元数据时，注入 <xref:Orleans.Runtime.MembershipService.SiloMetadata.ISiloMetadataCache> 和 <xref:Orleans.Runtime.IGrainRuntime>。使用 <xref:Orleans.Runtime.IGrainRuntime.SiloAddress?displayProperty=nameWithType> 确定当前激活实例所在的 Silo。读取元数据不会追溯性地改变放置结果。

<a id="implement-a-custom-filter"></a>
## 实现自定义筛选器

如果精确匹配元数据已足够，应优先使用内置筛选器。对于具有不同语义的策略（例如数值阈值），自定义筛选器很有用。它由三部分组成：

1. <xref:Orleans.Placement.PlacementFilterStrategy>，携带配置和唯一顺序值。
1. <xref:Orleans.Placement.PlacementFilterAttribute>，将策略附加到 Grain 类。
1. <xref:Orleans.Placement.IPlacementFilterDirector>，返回候选 <xref:Orleans.Runtime.SiloAddress> 值的一个子集。

以下示例要求候选 Silo 在 `hardware.cores` 元数据条目中声明的逻辑核心数至少达到指定数量。首先定义特性和策略：

:::code language="csharp" source="snippets/placement/CustomPlacementFilter.cs" id="custom_placement_filter_strategy":::

筛选器配置存储在 Grain 清单中，而不是随特性实例一同序列化。公共无参构造函数使依赖注入能够创建策略实例。<xref:Orleans.Placement.PlacementFilterStrategy.GetAdditionalGrainProperties*> 将配置写入清单，而 <xref:Orleans.Placement.PlacementFilterStrategy.AdditionalInitialize*> 在每个 Silo 上恢复并验证配置。

接下来，实现筛选决策器：

:::code language="csharp" source="snippets/placement/CustomPlacementFilter.cs" id="custom_placement_filter_director":::

筛选决策器会排除元数据缺失、格式错误或不满足要求的候选 Silo。如果没有候选 Silo 剩余，则放置失败，而不会悄然放宽要求。偏好筛选器则应在优先匹配结果为空时，显式返回适当的备选子集。

将筛选器应用于 Grain 类。放置策略仍然负责从剩余候选 Silo 中选择目标：

:::code language="csharp" source="snippets/placement/CustomPlacementFilter.cs" id="apply_custom_placement_filter":::

最后，在每个 Silo 上注册筛选器：

:::code language="csharp" source="snippets/placement/CustomPlacementFilter.cs" id="register_custom_placement_filter":::

<xref:Orleans.Placement.PlacementFilterExtensions.AddPlacementFilter*> 要求为策略指定 <xref:Microsoft.Extensions.DependencyInjection.ServiceLifetime>。此示例使用 `Transient`，因为初始化时会用 Grain 类型特有的配置修改策略实例。Orleans 按 Grain 类型缓存由此生成的策略。不论策略的生存期如何，决策器始终注册为带键的单例服务，因此必须是线程安全的，并使用适用于单例的依赖项。

仅从输入序列中返回候选 Silo；对于给定数据，保证筛选过程快速且确定，并监控硬性要求导致的放置失败。Silo 元数据是由运维人员提供的调度信息，而非实时利用率数据或安全边界。对于实时负载信号，应使用资源优化放置策略，并独立执行授权检查。

在放置期间，从 <xref:Orleans.Runtime.Placement.PlacementTarget.RequestContextData?displayProperty=nameWithType> 读取应用程序请求元数据；由于此时尚无激活实例，静态 <xref:Orleans.Runtime.RequestContext> 尚未填充。
