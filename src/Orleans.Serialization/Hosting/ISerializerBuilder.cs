using Microsoft.Extensions.DependencyInjection;

namespace Orleans.Serialization
{
    /// <summary>
    /// 配置序列化的构建界面 。
    /// </summary>
    public interface ISerializerBuilder
    {
        /// <summary>
        /// 收集服务收藏
        /// </summary>
        IServiceCollection Services { get; }
    }
}
