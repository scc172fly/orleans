using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Orleans.Hosting
{
    /// <summary>
    /// 用于配置 Orleans 服务端的构建器。
    /// </summary>
    public interface ISiloBuilder
    {
        /// <summary>
        /// Silo 与宿主共享的服务。
        /// </summary>
        IServiceCollection Services { get; }

        /// <summary>
        /// 获取配置。
        /// </summary>
        IConfiguration Configuration { get; }
    }
}
