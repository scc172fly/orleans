using System.Reflection;

namespace Orleans.Runtime
{
    /// <summary>
    /// 负责绘制一个可回收工厂的元数据
    /// </summary>
    public interface IAttributeToFactoryMapper<in TMetadata>
        where TMetadata : IFacetMetadata
    {
        /// <summary>
        /// 负责从参数和元数据向可分解工厂绘制一个元数据。
        /// </summary>
        /// <param name="parameter">参数信息。</param>
        /// <param name="metadata">元数据。</param>
        /// <returns>工厂用来为 grain创建表面实例。</returns>
        Factory<IGrainContext, object> GetFactory(ParameterInfo parameter, TMetadata metadata);
    }
}
