using System;
using Orleans;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Clustering.Redis;
using StackExchange.Redis;

namespace Microsoft.Extensions.Hosting
{
    /// <summary>
    /// 用于 Redis 集群成员管理提供程序的宿主扩展方法。
    /// </summary>
    public static class RedisClusteringISiloBuilderExtensions
    {
        /// <summary>
        /// 将 Redis 配置为集群成员管理提供程序。
        /// </summary>
        public static ISiloBuilder UseRedisClustering(this ISiloBuilder builder, Action<RedisClusteringOptions> configuration)
        {
            return builder.ConfigureServices(services =>
            {
                if (configuration != null)
                {
                    services.Configure(configuration);
                }

                services.AddRedisClustering();
            });
        }

        /// <summary>
        /// 将 Redis 配置为集群成员管理提供程序。
        /// </summary>
        public static ISiloBuilder UseRedisClustering(this ISiloBuilder builder, string redisConnectionString)
        {
            return builder.ConfigureServices(services => services
                .Configure<RedisClusteringOptions>(options =>
                {
                    options.ConfigurationOptions = ConfigurationOptions.Parse(redisConnectionString);
                })
                .AddRedisClustering());
        }

        internal static IServiceCollection AddRedisClustering(this IServiceCollection services)
        {
            services.AddSingleton<RedisMembershipTable>();
            services.AddSingleton<IConfigurationValidator, RedisClusteringOptionsValidator>();
            services.AddSingleton<IMembershipTable>(sp => sp.GetRequiredService<RedisMembershipTable>());
            return services;
        }
    }
}
