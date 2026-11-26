using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Orleans.Hosting
{
    /// <summary>
    /// 配置 Orleans 客户端的构建器。
    /// </summary>
    public class ClientBuilder : IClientBuilder
    {
        /// <summary>
        /// 初始化 <see cref="ClientBuilder"/> 类的新实例。
        /// </summary>
        /// <param name="services">
        /// 服务收藏
        /// </param>
        /// <param name="configuration">
        /// 客户配置。
        /// </param>
        public ClientBuilder(IServiceCollection services, IConfiguration configuration)
        {
            Services = services;
            Configuration = configuration;
            DefaultClientServices.AddDefaultServices(this);
        }

        /// <inheritdoc/>
        public IServiceCollection Services { get; }

        /// <inheritdoc/>
        public IConfiguration Configuration { get; }
    }
}
