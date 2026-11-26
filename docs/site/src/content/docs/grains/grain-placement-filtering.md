---
title: "Grain 放置过滤器"
description: "过滤器 Orleans grain 使用 silo 元数据。"
ms.date: 08/08/2026
ms.topic: concept-article
---

<a id="grain-placement-filters"></a>

# Grain 放置过滤器

> [!WARNING]
> 内置 <xref:Orleans.Runtime.Placement.Filtering.RequiredMatchSiloMetadataPlacementFilterAttribute> 和 <xref:Orleans.Runtime.Placement.Filtering.PreferredMatchSiloMetadataPlacementFilterAttribute> 是实验性的, 并附有 `Experimental("ORLEANSEXP004")`注释。 此诊断不适用于下文描述的自定义过滤器 API 。

放置首先决定哪个 silos 与一个 grain 类型兼容。 过滤器然后按照顺序在 grain [安置战略](grain-placement.md) 选择目标之前减少该候选人的集合 。

使用过滤器来限制和偏好, 如可用区、硬件能力或部署级别。 不要用它们编码每个要求的商业路线, 这将使一个 grain 身份的行为像多个独立实体一样。

<a id="configure-silo-metadata"></a>

## 配置 silo 元数据

内置过滤器比较 [silo 元元数据](../host/configuration-guide/silo-metadata.md) 电话 silo 候选人上带有元数据 silos配置每个参与者的一致键和值 silo:

:::code language="csharp" source="../snippets/compiled/Grains/PlacementSnippets.cs" id="configure_silo_metadata":::
外部客户没有 silo 元数据。设计过滤了 grain 激活路径,因此启动的安置请求来自有所需元数据的 silo 。

<a id="require-metadata-matches"></a>

## 需要符合元数据

<xref:Orleans.Runtime.Placement.Filtering.RequiredMatchSiloMetadataPlacementFilterAttribute> keeps only candidates matching every configured key:

:::code language="csharp" source="../snippets/compiled/Grains/PlacementSnippets.cs" id="required_metadata_filter":::
如果兼容 silo 匹配, 则放置失败。 只对硬性要求使用此过滤器 。

<a id="prefer-metadata-matches"></a>

## 更可取的元数据匹配

<xref:Orleans.Runtime.Placement.Filtering.PreferredMatchSiloMetadataPlacementFilterAttribute> prefers candidates matching the ordered keys and progressively falls back by dropping earlier keys:

:::code language="csharp" source="../snippets/compiled/Grains/PlacementSnippets.cs" id="preferred_metadata_filter":::
`minCandidates` 防止狭隘的偏好将位置集中在太少 silos上。

<a id="combine-and-order-filters"></a>

## 组合和订购过滤器

当 grain 类有多个过滤器时, 请给每个类别一个独有的 `order` 值。 Orleans 首先应用较低值。 属性声明命令不是一个可靠的订购机制 。

:::code language="csharp" source="../snippets/compiled/Grains/PlacementSnippets.cs" id="ordered_metadata_filters":::
职位安排策略只看到每个过滤器后留下的候选人。 一个首选过滤器只能在所接收的候选人范围内进行扩展; 它无法让先前要求的过滤器移除的候选人恢复原样 。

<a id="read-metadata-from-a-grain"></a>

## 从 grain读取元数据

注射 <xref:Orleans.Runtime.MembershipService.SiloMetadata.ISiloMetadataCache> 和 <xref:Orleans.Runtime.IGrainRuntime> 何时 grain 逻辑需要 silo 元元数据。 <xref:Orleans.Runtime.IGrainRuntime.SiloAddress?displayProperty=nameWithType> 来识别当前激活的 silo元数据读取时不会影响追溯性定位。

<a id="implement-a-custom-filter"></a>

## 执行自定义过滤器

当精确的元数据匹配足够时,更倾向于内置过滤器。 定制过滤器对具有不同语义学的政策有用, 如数字阈值。 它有三个部分:

1. 带有配置和独特顺序的 <xref:Orleans.Placement.PlacementFilterStrategy> 。
1. A <xref:Orleans.Placement.PlacementFilterAttribute> 战略应把战略与 grain 类.
1. A 音 A <xref:Orleans.Placement.IPlacementFilterDirector> 返回候选人的子集 <xref:Orleans.Runtime.SiloAddress> 值。

以下例子要求候选人在 `hardware.cores` silo 元数据条目中公布最低限度逻辑核心计算。

:::code language="csharp" source="snippets/placement/CustomPlacementFilter.cs" id="custom_placement_filter_strategy":::

过滤器配置存储在 grain 状态表,而不是与属性实例序列。公共无参数构造器允许依赖性注射创建策略。 <xref:Orleans.Placement.PlacementFilterStrategy.GetAdditionalGrainProperties*> 将配置写入列表, <xref:Orleans.Placement.PlacementFilterStrategy.AdditionalInitialize*> 在每个 silo上恢复并验证它。

执行主任:

:::code language="csharp" source="snippets/placement/CustomPlacementFilter.cs" id="custom_placement_filter_director":::

主任排除了缺少、错误或元数据不足的候选人。 如果剩下的没有,职位安排就失败了,而不是默默地削弱要求。 当首选结果为空时,偏好过滤器应该明确返回适当的后备子集。

将过滤器应用到 grain 类。一个职位安排策略仍然从剩下的候选人中选择:

:::code language="csharp" source="snippets/placement/CustomPlacementFilter.cs" id="apply_custom_placement_filter":::

最后,在每 silo上登记过滤器:

:::code language="csharp" source="snippets/placement/CustomPlacementFilter.cs" id="register_custom_placement_filter":::

<xref:Orleans.Placement.PlacementFilterExtensions.AddPlacementFilter*> 需要战略的 <xref:Microsoft.Extensions.DependencyInjection.ServiceLifetime> 。此示例使用 `Transient` ,因为初始化使战略发生突变时有 grain- 类型配置。 Orleans 将由此产生的策略隐藏在每类型 grain 的缓存中。主任总是登记为键入单吨,不管战略寿命长短,因此它必须是安全线并使用单一安全度依赖性。

仅将候选人从输入序列中返回,不断快速筛选所提供数据并确定性,并监测因硬性要求造成的安置失误。 Silo 元数据是操作员提供的时间安排信息,不是实时利用数据或安全边界。使用资源优化的实时载荷信号定位并独立执行授权。

放置期间,读取应用程序请求从 <xref:Orleans.Runtime.Placement.PlacementTarget.RequestContextData?displayProperty=nameWithType>获取元数据;静态 <xref:Orleans.Runtime.RequestContext> 没有被控制,因为还没有激活。
