using System;
using System.Collections.Generic;
using Orleans.Metadata;
using Orleans.Placement;

namespace Orleans.Runtime.Placement.Filtering;

/// <summary>
/// 配置放置筛选策略，要求 Silo 的所有指定元数据键均与调用方 Silo 匹配。
/// </summary>
/// <param name="metadataKeys">其值必须与调用方 Silo 一致的元数据键。</param>
/// <param name="order">此筛选器相对于其他放置筛选器的应用顺序。</param>
public class RequiredMatchSiloMetadataPlacementFilterStrategy(string[] metadataKeys, int order)
    : PlacementFilterStrategy(order)
{
    /// <summary>
    /// 获取其值必须与调用方 Silo 一致的元数据键。
    /// </summary>
    public string[] MetadataKeys { get; private set; } = metadataKeys;

    /// <summary>
    /// 初始化 <see cref="RequiredMatchSiloMetadataPlacementFilterStrategy"/> 类的新实例，
    /// 不指定元数据键，并使用默认筛选器应用顺序。
    /// </summary>
    public RequiredMatchSiloMetadataPlacementFilterStrategy() : this([], 0)
    {
    }

    /// <inheritdoc />
    public override void AdditionalInitialize(GrainProperties properties)
    {
        var placementFilterGrainProperty = GetPlacementFilterGrainProperty("metadata-keys", properties);
        if (placementFilterGrainProperty is null)
        {
            throw new ArgumentException("Invalid metadata-keys property value.");
        }
        MetadataKeys = placementFilterGrainProperty.Split(",");
    }

    /// <inheritdoc />
    protected override IEnumerable<KeyValuePair<string, string>> GetAdditionalGrainProperties(IServiceProvider services, Type grainClass, GrainType grainType,
        IReadOnlyDictionary<string, string> existingProperties)
    {
        yield return new KeyValuePair<string, string>("metadata-keys", String.Join(",", MetadataKeys));
    }
}
