using Microsoft.Extensions.DependencyInjection;

namespace Orleans.Serialization
{
    /// <summary>
    /// 用于配置序列化的构建器接口。
    /// </summary>
    public interface ISerializerBuilder
    {
        /// <summary>
        /// 获取服务集合。
        /// </summary>
        IServiceCollection Services { get; }
    }
}
