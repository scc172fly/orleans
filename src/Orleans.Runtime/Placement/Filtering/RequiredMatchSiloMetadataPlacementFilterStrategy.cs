using System;
using System.Collections.Generic;
using Orleans.Metadata;
using Orleans.Placement;

namespace Orleans.Runtime.Placement.Filtering;

/// <summary>
/// 配置配置布局过滤程序,该过滤程序要求 silos 匹配每个指定密钥的调用 silo元数据。
/// </summary>
/// <param name="metadataKeys">元数据键的数值必须与调用 silo的数值相符。</param>
/// <param name="order">相对于其他放置过滤器应用此过滤器的顺序。</param>
public class RequiredMatchSiloMetadataPlacementFilterStrategy(string[] metadataKeys, int order)
    : PlacementFilterStrategy(order)
{
    /// <summary>
    /// 获取其值必须与调用 silo的数值相符的元数据键。
    /// </summary>
    public string[] MetadataKeys { get; private set; } = metadataKeys;

    /// <summary>
    /// 初始化 <see cref="RequiredMatchSiloMetadataPlacementFilterStrategy"/> 类的新实例
    /// 没有元数据键和默认过滤顺序。
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
