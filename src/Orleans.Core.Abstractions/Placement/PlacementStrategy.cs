using System;
using System.Collections.Generic;
using Orleans.Metadata;

namespace Orleans.Runtime
{
    /// <summary>
    /// 所有安置战略的基础类型。
    /// </summary>
    /// <remarks>
    /// Orleans 使用可配置的定位系统决定安装哪个服务器 grain 。
    /// 由职位安排主任决定应在何处启动 grain 激活。
    /// 采用安置策略,与 grains 职位的安置主任有联系。
    /// Grains 使用 grain 类的属性表示其首选安置策略。
    /// </remarks>
    [Serializable, SerializerTransparent]
    public abstract class PlacementStrategy
    {
        /// <summary>
        /// 获得一个值, 显示是否该安置策略要求启动程序注册
        /// grain 目录。
        /// </summary>
        public virtual bool IsUsingGrainDirectory => true;

        /// <summary>
        /// 使用所提供的 grain 属性初始化这种类型的实例。
        /// </summary>
        /// <param name="properties">
        /// grain 属性。
        /// </param>
        public virtual void Initialize(GrainProperties properties)
        {
        }

        /// <summary>
        /// 弹出 grain 属性以指定首选安插策略。
        /// </summary>
        /// <param name="services">服务供应商。</param>
        /// <param name="grainClass"> grain 类。</param>
        /// <param name="grainType"> grain 类型。</param>
        /// <param name="properties">将使用此方法调用来填充的 grain 属性。</param>
        public virtual void PopulateGrainProperties(IServiceProvider services, Type grainClass, GrainType grainType, Dictionary<string, string> properties)
        {
            properties[WellKnownGrainTypeProperties.PlacementStrategy] = this.GetType().Name;
        }
    }
}
