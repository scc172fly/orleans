using System;
using System.Collections.Generic;
using Orleans.Metadata;

namespace Orleans.Runtime
{
    /// <summary>
    /// 所有放置策略的基类型。
    /// </summary>
    /// <remarks>
    /// Orleans 使用可配置的放置系统来决定将 Grain 放置在哪台服务器上。
    /// 放置决策器用于确定 Grain 激活实例的放置位置。
    /// 通过放置策略将放置决策器与 Grain 关联。
    /// Grain 通过其类上的特性指定首选放置策略。
    /// </remarks>
    [Serializable, SerializerTransparent]
    public abstract class PlacementStrategy
    {
        /// <summary>
        /// 获取一个值，指示此放置策略是否要求将激活实例注册到
        /// Grain 目录中。
        /// </summary>
        public virtual bool IsUsingGrainDirectory => true;

        /// <summary>
        /// 使用提供的 Grain 属性初始化此类型的实例。
        /// </summary>
        /// <param name="properties">
        /// Grain 属性。
        /// </param>
        public virtual void Initialize(GrainProperties properties)
        {
        }

        /// <summary>
        /// 填充 Grain 属性以指定首选放置策略。
        /// </summary>
        /// <param name="services">服务提供程序。</param>
        /// <param name="grainClass">Grain 类。</param>
        /// <param name="grainType">Grain 类型。</param>
        /// <param name="properties">此方法调用将填充的 Grain 属性。</param>
        public virtual void PopulateGrainProperties(IServiceProvider services, Type grainClass, GrainType grainType, Dictionary<string, string> properties)
        {
            properties[WellKnownGrainTypeProperties.PlacementStrategy] = this.GetType().Name;
        }
    }
}
