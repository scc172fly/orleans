using System;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Messaging;
using Orleans.Clustering.Redis;
using StackExchange.Redis;

namespace Microsoft.Extensions.Hosting
{
    /// <summary>
    /// 用于 Redis 集群配置的宿主扩展方法。
    /// </summary>
    public static class RedisClusteringIClientBuilderExtensions
    {
        /// <summary>
        /// 将 Redis 配置为集群成员管理提供程序。
        /// </summary>
        public static IClientBuilder UseRedisClustering(this IClientBuilder builder, Action<RedisClusteringOptions> configuration)
        {
            return builder.ConfigureServices(services =>
            {
                if (configuration != null)
                {
                    services.Configure(configuration);
                }

                services
                    .AddRedisClustering()
                    .AddSingleton<IGatewayListProvider, RedisGatewayListProvider>();
            });
        }

        /// <summary>
        /// 将 Redis 配置为集群成员管理提供程序。
        /// </summary>
        public static IClientBuilder UseRedisClustering(this IClientBuilder builder, string redisConnectionString)
        {
            return builder.ConfigureServices(services => services
                .Configure<RedisClusteringOptions>(opt =>
                {
                    opt.ConfigurationOptions = ConfigurationOptions.Parse(redisConnectionString);
                })
                .AddRedisClustering()
                .AddSingleton<IGatewayListProvider, RedisGatewayListProvider>());
        }

    }
}
