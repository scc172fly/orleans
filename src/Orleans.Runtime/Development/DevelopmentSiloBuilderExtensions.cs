using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.LeaseProviders;

namespace Orleans.Runtime.Development
{
    /// <summary>
    /// 用于配置内存租约提供程序的 <see cref="ISiloBuilder"/> 扩展方法。
    /// </summary>
    public static class DevelopmentSiloBuilderExtensions
    {
        /// <summary>
        /// 使用测试或开发功能配置 Silo。
        /// </summary>
        /// <remarks>
        /// 不适用于生产环境，仅供开发及测试场景使用。
        /// </remarks>
        /// <param name="builder">构建器。</param>
        /// <returns>构建器。</returns>
        public static ISiloBuilder UseInMemoryLeaseProvider(this ISiloBuilder builder)
        {
            return builder.ConfigureServices(UseInMemoryLeaseProvider);
        }

        private static void UseInMemoryLeaseProvider(IServiceCollection services)
        {
            services.AddTransient<ILeaseProvider, InMemoryLeaseProvider>();
        }
    }
}
