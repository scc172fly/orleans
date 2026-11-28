using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Orleans.Hosting
{
    /// <summary>
    /// 用于配置 Orleans 服务端的构建器。
    /// </summary>
    internal class SiloBuilder : ISiloBuilder
    {
        public SiloBuilder(IServiceCollection services, IConfiguration configuration)
        {
            Services = services;
            Configuration = configuration;
            DefaultSiloServices.AddDefaultServices(this);
        }

        public IServiceCollection Services { get; }
        public IConfiguration Configuration { get; }
    }
}
