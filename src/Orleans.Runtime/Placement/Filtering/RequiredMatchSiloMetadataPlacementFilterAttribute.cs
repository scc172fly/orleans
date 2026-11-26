using System;
using System.Diagnostics.CodeAnalysis;
using Orleans.Placement;

namespace Orleans.Runtime.Placement.Filtering;

/// <summary>
/// 属性可以指定, silo 必须有一个与本地(调用) silo 相匹配的特定元数据关键值对, 以考虑放置。
/// </summary>
/// <param name="metadataKeys">元数据键的数值必须与调用 silo的数值相符。</param>
/// <param name="order">相对于其他放置过滤器应用此过滤器的顺序。</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
[Experimental("ORLEANSEXP004")]
public class RequiredMatchSiloMetadataPlacementFilterAttribute(string[] metadataKeys, int order = 0)
    : PlacementFilterAttribute(new RequiredMatchSiloMetadataPlacementFilterStrategy(metadataKeys, order));
