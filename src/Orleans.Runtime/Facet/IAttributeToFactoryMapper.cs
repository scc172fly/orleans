using System.Reflection;

namespace Orleans.Runtime
{
    /// <summary>
    /// 负责将 facet 元数据映射到可缓存的工厂。
    /// </summary>
    public interface IAttributeToFactoryMapper<in TMetadata>
        where TMetadata : IFacetMetadata
    {
        /// <summary>
        /// 根据参数及 facet 元数据，将 facet 元数据映射到可缓存的工厂。
        /// </summary>
        /// <param name="parameter">参数信息。</param>
        /// <param name="metadata">元数据。</param>
        /// <returns>用于为 Grain 创建 facet 实例的工厂。</returns>
        Factory<IGrainContext, object> GetFactory(ParameterInfo parameter, TMetadata metadata);
    }
}
