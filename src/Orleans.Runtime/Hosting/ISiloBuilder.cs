using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Orleans.Hosting
{
    /// <summary>
    /// 配置 Orleans 服务器的构建器。
    /// </summary>
    public interface ISiloBuilder
    {
        /// <summary>
        /// 由 silo 和东道主共享的服务。
        /// </summary>
        IServiceCollection Services { get; }

        /// <summary>
        /// 得到配置。
        /// </summary>
        IConfiguration Configuration { get; }
    }
}
