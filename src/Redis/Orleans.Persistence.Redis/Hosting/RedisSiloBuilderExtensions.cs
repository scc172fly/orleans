using System;
using Microsoft.Extensions.Options;
using Orleans.Persistence;
using Orleans.Providers;

namespace Orleans.Hosting
{
    /// <summary>
    /// <see cref="ISiloBuilder"/> 扩展。
    /// </summary>
    public static class RedisSiloBuilderExtensions
    {
        /// <summary>
        /// 配置 Redis 作为默认的 grain 存储提供者 。
        /// </summary>
        public static ISiloBuilder AddRedisGrainStorageAsDefault(this ISiloBuilder builder, Action<RedisStorageOptions> configureOptions)
        {
            return builder.AddRedisGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
        }

        /// <summary>
        /// 配置 Redis 作为 grain 存储提供者的配置。
        /// </summary>
        public static ISiloBuilder AddRedisGrainStorage(this ISiloBuilder builder, string name, Action<RedisStorageOptions> configureOptions)
        {
            return builder.ConfigureServices(services => services.AddRedisGrainStorage(name, configureOptions));
        }

        /// <summary>
        /// 配置 Redis 作为默认的 grain 存储提供者 。
        /// </summary>
        public static ISiloBuilder AddRedisGrainStorageAsDefault(this ISiloBuilder builder)
            => builder.AddRedisGrainStorageAsDefault(configureOptionsBuilder: null);

        /// <summary>
        /// 配置 Redis 作为默认的 grain 存储提供者 。
        /// </summary>
        public static ISiloBuilder AddRedisGrainStorageAsDefault(this ISiloBuilder builder, Action<OptionsBuilder<RedisStorageOptions>>? configureOptionsBuilder)
        {
            return builder.AddRedisGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptionsBuilder);
        }

        /// <summary>
        /// 配置 Redis 作为 grain 存储提供者的配置。
        /// </summary>
        public static ISiloBuilder AddRedisGrainStorage(this ISiloBuilder builder, string name)
            => builder.AddRedisGrainStorage(name, configureOptionsBuilder: null);

        /// <summary>
        /// 配置 Redis 作为 grain 存储提供者的配置。
        /// </summary>
        public static ISiloBuilder AddRedisGrainStorage(this ISiloBuilder builder, string name, Action<OptionsBuilder<RedisStorageOptions>>? configureOptionsBuilder)
        {
            return builder.ConfigureServices(services => services.AddRedisGrainStorage(name, configureOptionsBuilder));
        }
    }
}
