using System;
using System.Collections.Generic;
using System.Globalization;
using Orleans.Metadata;
using Orleans.Runtime;

namespace Orleans.Placement;

/// <summary>
/// 表示筛选可放置 Grain 的 Silo 的策略。
/// </summary>
public abstract class PlacementFilterStrategy
{
    /// <summary>
    /// 获取此过滤器相对于其他放置过滤器的应用顺序。
    /// </summary>
    public int Order { get; private set; }

    /// <summary>
    /// 初始化 <see cref="PlacementFilterStrategy"/> 类的新实例。
    /// </summary>
    /// <param name="order">此过滤器相对于其他放置过滤器的应用顺序。</param>
    protected PlacementFilterStrategy(int order)
    {
        Order = order;
    }

    /// <summary>
    /// 使用提供的 Grain 属性初始化此类型的实例。
    /// </summary>
    /// <param name="properties">
    /// Grain 属性。
    /// </param>
    public void Initialize(GrainProperties properties)
    {
        var orderProperty = GetPlacementFilterGrainProperty("order", properties);
        if (!int.TryParse(orderProperty, out var parsedOrder))
        {
            throw new ArgumentException("Invalid order property value.");
        }

        Order = parsedOrder;

        AdditionalInitialize(properties);
    }

    /// <summary>
    /// 使用提供的 Grain 属性初始化策略特有的状态。
    /// </summary>
    /// <param name="properties">Grain 属性。</param>
    public virtual void AdditionalInitialize(GrainProperties properties)
    {
    }

    /// <summary>
    /// 填充 Grain 属性以指定首选放置策略。
    /// </summary>
    /// <param name="services">服务提供程序。</param>
    /// <param name="grainClass">Grain 类。</param>
    /// <param name="grainType">Grain 类型。</param>
    /// <param name="properties">此方法调用将填充的 Grain 属性。</param>
    public void PopulateGrainProperties(IServiceProvider services, Type grainClass, GrainType grainType, Dictionary<string, string> properties)
    {
        var typeName = GetType().Name;
        if (properties.TryGetValue(WellKnownGrainTypeProperties.PlacementFilter, out var existingValue))
        {
            properties[WellKnownGrainTypeProperties.PlacementFilter] = $"{existingValue},{typeName}";
        }
        else
        {
            properties[WellKnownGrainTypeProperties.PlacementFilter] = typeName;
        }

        properties[$"{WellKnownGrainTypeProperties.PlacementFilter}.{typeName}.order"] = Order.ToString(CultureInfo.InvariantCulture);

        foreach (var additionalGrainProperty in GetAdditionalGrainProperties(services, grainClass, grainType, properties))
        {
            properties[$"{WellKnownGrainTypeProperties.PlacementFilter}.{typeName}.{additionalGrainProperty.Key}"] = additionalGrainProperty.Value;
        }
    }

    /// <summary>
    /// 获取此放置过滤策略的一项属性。
    /// </summary>
    /// <param name="key">策略特有的属性键。</param>
    /// <param name="properties">Grain 属性。</param>
    /// <returns>属性值；如果属性不存在，则为 <see langword="null"/>。</returns>
    protected string? GetPlacementFilterGrainProperty(string key, GrainProperties properties)
    {
        var typeName = GetType().Name;
        return properties.Properties.TryGetValue($"{WellKnownGrainTypeProperties.PlacementFilter}.{typeName}.{key}", out var value) ? value : null;
    }

    /// <summary>
    /// 获取要填充的策略特有的 Grain 属性。
    /// </summary>
    /// <param name="services">服务提供程序。</param>
    /// <param name="grainClass">Grain 类。</param>
    /// <param name="grainType">Grain 类型。</param>
    /// <param name="existingProperties">到目前为止已填充的 Grain 属性。</param>
    /// <returns>策略特有的 Grain 属性。</returns>
    protected virtual IEnumerable<KeyValuePair<string, string>> GetAdditionalGrainProperties(IServiceProvider services, Type grainClass, GrainType grainType, IReadOnlyDictionary<string, string> existingProperties)
        => Array.Empty<KeyValuePair<string, string>>();
}
