using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Orleans.Hosting
{
    /// <summary>
    /// 配置 Orleans 客户端的构建器。
    /// </summary>
    public interface IClientBuilder
    {
        /// <summary>
        /// 收集服务
        /// </summary>
        IServiceCollection Services { get; }

        /// <summary>
        /// 得到配置。
        /// </summary>
        IConfiguration Configuration { get; }
    }
}
