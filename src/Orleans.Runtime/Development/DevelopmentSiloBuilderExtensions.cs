using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.LeaseProviders;

namespace Orleans.Runtime.Development
{
    /// <summary>
    /// 配置内置租赁提供人的<see cref="ISiloBuilder"/> 扩展 。
    /// </summary>
    public static class DevelopmentSiloBuilderExtensions
    {
        /// <summary>
        /// 具有测试/开发功能的配置 silo 。
        /// </summary>
        /// <remarks>
        /// 仅用于发展和测试设想。
        /// </remarks>
        /// <param name="builder">建筑工。</param>
        /// <returns>建筑工。</returns>
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
