using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Orleans.Hosting
{
    /// <summary>
    /// 用于配置 Orleans 客户端的生成器。
    /// </summary>
    public interface IClientBuilder
    {
        /// <summary>
        /// 获取服务集合。
        /// </summary>
        IServiceCollection Services { get; }

        /// <summary>
        /// 获取配置。
        /// </summary>
        IConfiguration Configuration { get; }
    }
}
