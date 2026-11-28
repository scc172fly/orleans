using System;
using System.Diagnostics.CodeAnalysis;
using Orleans.Placement;

namespace Orleans.Runtime.Placement.Filtering;

/// <summary>
/// 用于指定优先匹配 Silo 元数据的放置筛选器的特性；该筛选器优先选择元数据与本地（调用方）Silo 一致的 Silo。
/// </summary>
/// <param name="orderedMetadataKeys">按顺序尝试匹配的元数据键集合。靠前的键重要性较低；如果更具体的匹配结果不足，则逐步舍弃这些键以获得较宽松的匹配。</param>
/// <param name="minCandidates">希望筛选后保留的最少候选数，用于在元数据偏好与避免少量 Silo 承担过多激活实例之间取得平衡。若只需最佳匹配结果，即使当前只有一个最佳匹配的 Silo，也可设为 1。</param>
/// <param name="order">此筛选器相对于其他放置筛选器的应用顺序。</param>
/// <remarks>示例：指定键 ["first","second"] 时，先尝试仅返回两个键均与本地 Silo 元数据值相匹配的 Silo。若数量不足，则同时纳入仅匹配 second 键的 Silo。若结果仍少于 minCandidates，则纳入全部 Silo。</remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
[Experimental("ORLEANSEXP004")]
public class PreferredMatchSiloMetadataPlacementFilterAttribute(string[] orderedMetadataKeys, int minCandidates = 2, int order = 0)
    : PlacementFilterAttribute(new PreferredMatchSiloMetadataPlacementFilterStrategy(orderedMetadataKeys, minCandidates, order));
