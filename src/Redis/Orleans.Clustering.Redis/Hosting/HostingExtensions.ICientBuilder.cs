using System;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Messaging;
using Orleans.Clustering.Redis;
using StackExchange.Redis;

namespace Microsoft.Extensions.Hosting
{
    /// <summary>
    /// Redis 集束的主机扩展名。
    /// </summary>
    public static class RedisClusteringIClientBuilderExtensions
    {
        /// <summary>
        /// 配置 Redis 作为组合集提供者。
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
        /// 配置 Redis 作为组合集提供者。
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
