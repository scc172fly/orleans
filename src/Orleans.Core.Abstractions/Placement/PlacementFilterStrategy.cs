using System;
using System.Collections.Generic;
using System.Globalization;
using Orleans.Metadata;
using Orleans.Runtime;

namespace Orleans.Placement;

/// <summary>
/// 表示过滤 silos 的战略,可以安装一个 grain 。
/// </summary>
public abstract class PlacementFilterStrategy
{
    /// <summary>
    /// 获得这个过滤器相对于其他放置过滤器应用的顺序。
    /// </summary>
    public int Order { get; private set; }

    /// <summary>
    /// 初始化 <see cref="PlacementFilterStrategy"/> 类的新实例。
    /// </summary>
    /// <param name="order">相对于其他放置过滤器应用此过滤器的顺序。</param>
    protected PlacementFilterStrategy(int order)
    {
        Order = order;
    }

    /// <summary>
    /// 使用所提供的 grain 属性初始化这种类型的实例。
    /// </summary>
    /// <param name="properties">
    /// grain 属性。
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
    /// 使用所提供的 grain 属性,初始化特定战略状态。
    /// </summary>
    /// <param name="properties"> grain 属性。</param>
    public virtual void AdditionalInitialize(GrainProperties properties)
    {
    }

    /// <summary>
    /// 弹出 grain 属性以指定首选安插策略。
    /// </summary>
    /// <param name="services">服务供应商。</param>
    /// <param name="grainClass"> grain 类。</param>
    /// <param name="grainType"> grain 类型。</param>
    /// <param name="properties">将使用此方法调用来填充的 grain 属性。</param>
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
    /// 获取用于此放置过滤策略的属性 。
    /// </summary>
    /// <param name="key">特定财产战略关键部分。</param>
    /// <param name="properties"> grain 属性。</param>
    /// <returns>财产价值,或 <see langword="null"/> 如果财产不存在。</returns>
    protected string? GetPlacementFilterGrainProperty(string key, GrainProperties properties)
    {
        var typeName = GetType().Name;
        return properties.Properties.TryGetValue($"{WellKnownGrainTypeProperties.PlacementFilter}.{typeName}.{key}", out var value) ? value : null;
    }

    /// <summary>
    /// 获取战略特定 grain 属性以弹出。
    /// </summary>
    /// <param name="services">服务供应商。</param>
    /// <param name="grainClass"> grain 类。</param>
    /// <param name="grainType"> grain 类型。</param>
    /// <param name="existingProperties">下 grain 至今人口密集的地产。</param>
    /// <returns>具体战略 grain 属性。</returns>
    protected virtual IEnumerable<KeyValuePair<string, string>> GetAdditionalGrainProperties(IServiceProvider services, Type grainClass, GrainType grainType, IReadOnlyDictionary<string, string> existingProperties)
        => Array.Empty<KeyValuePair<string, string>>();
}
