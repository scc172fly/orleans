using System;
using System.Diagnostics.CodeAnalysis;
using Orleans.Placement;

namespace Orleans.Runtime.Placement.Filtering;

/// <summary>
/// 用于指定 Silo 必须具有与本地（调用方）Silo 相匹配的特定元数据键值对，才可作为放置候选项的特性。
/// </summary>
/// <param name="metadataKeys">其值必须与调用方 Silo 一致的元数据键。</param>
/// <param name="order">此筛选器相对于其他放置筛选器的应用顺序。</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
[Experimental("ORLEANSEXP004")]
public class RequiredMatchSiloMetadataPlacementFilterAttribute(string[] metadataKeys, int order = 0)
    : PlacementFilterAttribute(new RequiredMatchSiloMetadataPlacementFilterStrategy(metadataKeys, order));
