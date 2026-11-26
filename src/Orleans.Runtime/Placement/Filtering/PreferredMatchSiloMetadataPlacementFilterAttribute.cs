using System;
using System.Diagnostics.CodeAnalysis;
using Orleans.Placement;

namespace Orleans.Runtime.Placement.Filtering;

/// <summary>
/// 属性可以指定首选匹配的 silo 元数据放置过滤器,该过滤器优先筛选到 silos ,而当元数据与本地(调用) silo 元数据相匹配时。
/// </summary>
/// <param name="orderedMetadataKeys">一组有顺序的元数据键,以试图匹配。早期条目被认为不太重要,如果足够具体匹配没有足够结果,将丢弃它们寻找不那么具体的匹配。</param>
/// <param name="minCandidates">需要过滤的最低理想候选人。 这是为了平衡满足元数据偏好与不因激活而超载一小组或一组 silos 的元数据偏好。 如果您只想要最佳匹配, 请将此设为 1, 即使目前只有一个 silo 最符合 。</param>
/// <param name="order">相对于其他放置过滤器应用此过滤器的顺序。</param>
/// <remarks>示例: 如果指定密钥 [“第一个” “第二 ” , 它将只尝试返回 silos 两种键都匹配本地 silo元数据值。如果数据不充分, silos 匹配对齐, 并包含 silos 仅匹配第二个密钥。最后,如果仍然小于 minCandidates 包括所有结果 silos.</remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
[Experimental("ORLEANSEXP004")]
public class PreferredMatchSiloMetadataPlacementFilterAttribute(string[] orderedMetadataKeys, int minCandidates = 2, int order = 0)
    : PlacementFilterAttribute(new PreferredMatchSiloMetadataPlacementFilterStrategy(orderedMetadataKeys, minCandidates, order));
