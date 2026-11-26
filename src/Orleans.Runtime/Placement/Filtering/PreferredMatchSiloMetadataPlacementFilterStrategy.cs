using System;
using System.Collections.Generic;
using System.Globalization;
using Orleans.Metadata;
using Orleans.Placement;

namespace Orleans.Runtime.Placement.Filtering;

/// <summary>
/// 配置更喜欢的放置过滤器 silos 元数据符合要求 silo元数据
/// </summary>
/// <param name="orderedMetadataKeys">
/// 匹配的元数据键, 从最小点订购到最要紧。 次要点被逐渐删除
/// 至少 <paramref name="minCandidates"/> 候选人留任。
/// </param>
/// <param name="minCandidates">理想的最低安置候选人人数。</param>
/// <param name="order">相对于其他放置过滤器应用此过滤器的顺序。</param>
public class PreferredMatchSiloMetadataPlacementFilterStrategy(string[] orderedMetadataKeys, int minCandidates, int order)
    : PlacementFilterStrategy(order)
{
    /// <summary>
    /// 获取或设置元数据键以匹配,从最小到最重要的命令。
    /// </summary>
    public string[] OrderedMetadataKeys { get; set; } = orderedMetadataKeys;

    /// <summary>
    /// 获得或确定理想的最低安置候选人人数。
    /// </summary>
    public int MinCandidates { get; set; } = minCandidates;

    /// <summary>
    /// 初始化 <see cref="PreferredMatchSiloMetadataPlacementFilterStrategy"/> 类的新实例
    /// 没有元数据键, 至少有两个候选人, 以及默认过滤顺序 。
    /// </summary>
    public PreferredMatchSiloMetadataPlacementFilterStrategy() : this([], 2, 0)
    {
    }

    /// <inheritdoc />
    public override void AdditionalInitialize(GrainProperties properties)
    {
        var placementFilterGrainProperty = GetPlacementFilterGrainProperty("ordered-metadata-keys", properties);
        if (placementFilterGrainProperty is null)
        {
            throw new ArgumentException("Invalid ordered-metadata-keys property value.");
        }

        OrderedMetadataKeys = placementFilterGrainProperty.Split(",");
        var minCandidatesProperty = GetPlacementFilterGrainProperty("min-candidates", properties);
        if (!int.TryParse(minCandidatesProperty, out var parsedMinCandidates))
        {
            throw new ArgumentException("Invalid min-candidates property value.");
        }

        MinCandidates = parsedMinCandidates;
    }

    /// <inheritdoc />
    protected override IEnumerable<KeyValuePair<string, string>> GetAdditionalGrainProperties(IServiceProvider services, Type grainClass, GrainType grainType,
        IReadOnlyDictionary<string, string> existingProperties)
    {
        yield return new KeyValuePair<string, string>("ordered-metadata-keys", string.Join(",", OrderedMetadataKeys));
        yield return new KeyValuePair<string, string>("min-candidates", MinCandidates.ToString(CultureInfo.InvariantCulture));
    }
}
