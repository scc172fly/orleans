using System;
using System.Collections.Generic;
using System.Globalization;
using Orleans.Metadata;
using Orleans.Placement;

namespace Orleans.Runtime.Placement.Filtering;

/// <summary>
/// 配置放置筛选策略，优先选择元数据与调用方 Silo 一致的 Silo。
/// </summary>
/// <param name="orderedMetadataKeys">
/// 要匹配的元数据键，按重要性从低到高排列。逐步省略较不重要的键，
/// 直到至少剩下 <paramref name="minCandidates"/> 个候选项。
/// </param>
/// <param name="minCandidates">期望的最少放置候选数。</param>
/// <param name="order">此筛选器相对于其他放置筛选器的应用顺序。</param>
public class PreferredMatchSiloMetadataPlacementFilterStrategy(string[] orderedMetadataKeys, int minCandidates, int order)
    : PlacementFilterStrategy(order)
{
    /// <summary>
    /// 获取或设置要匹配的元数据键，按重要性从低到高排列。
    /// </summary>
    public string[] OrderedMetadataKeys { get; set; } = orderedMetadataKeys;

    /// <summary>
    /// 获取或设置期望的最少放置候选数。
    /// </summary>
    public int MinCandidates { get; set; } = minCandidates;

    /// <summary>
    /// 初始化 <see cref="PreferredMatchSiloMetadataPlacementFilterStrategy"/> 类的新实例，
    /// 不指定元数据键，最少候选数为 2，并使用默认筛选器应用顺序。
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
