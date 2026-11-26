using System;
using System.Collections.Generic;
using Orleans.Metadata;
using Orleans.Runtime;

namespace Orleans.Placement;

/// <summary>
/// 用于所有放置过滤标记属性的基础 。
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public abstract class PlacementFilterAttribute : Attribute, IGrainPropertiesProviderAttribute
{
    /// <summary>
    /// 获得职位布局过滤策略。
    /// </summary>
    public PlacementFilterStrategy PlacementFilterStrategy { get; private set; }

    /// <summary>
    /// 初始化 <see cref="PlacementFilterAttribute"/> 类的新实例。
    /// </summary>
    /// <param name="placement">与此属性相关的放置过滤策略。</param>
    protected PlacementFilterAttribute(PlacementFilterStrategy placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        PlacementFilterStrategy = placement;
    }

    /// <inheritdoc />
    public virtual void Populate(IServiceProvider services, Type grainClass, GrainType grainType, Dictionary<string, string> properties)
        => PlacementFilterStrategy?.PopulateGrainProperties(services, grainClass, grainType, properties);
}
