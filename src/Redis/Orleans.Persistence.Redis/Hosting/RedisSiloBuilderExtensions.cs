using System;
using Microsoft.Extensions.Options;
using Orleans.Persistence;
using Orleans.Providers;

namespace Orleans.Hosting
{
    /// <summary>
    /// <see cref="ISiloBuilder"/> 的扩展方法。
    /// </summary>
    public static class RedisSiloBuilderExtensions
    {
        /// <summary>
        /// 将 Redis 配置为默认 Grain 存储提供程序。
        /// </summary>
        public static ISiloBuilder AddRedisGrainStorageAsDefault(this ISiloBuilder builder, Action<RedisStorageOptions> configureOptions)
        {
            return builder.AddRedisGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
        }

        /// <summary>
        /// 将 Redis 配置为 Grain 存储提供程序。
        /// </summary>
        public static ISiloBuilder AddRedisGrainStorage(this ISiloBuilder builder, string name, Action<RedisStorageOptions> configureOptions)
        {
            return builder.ConfigureServices(services => services.AddRedisGrainStorage(name, configureOptions));
        }

        /// <summary>
        /// 将 Redis 配置为默认 Grain 存储提供程序。
        /// </summary>
        public static ISiloBuilder AddRedisGrainStorageAsDefault(this ISiloBuilder builder)
            => builder.AddRedisGrainStorageAsDefault(configureOptionsBuilder: null);

        /// <summary>
        /// 将 Redis 配置为默认 Grain 存储提供程序。
        /// </summary>
        public static ISiloBuilder AddRedisGrainStorageAsDefault(this ISiloBuilder builder, Action<OptionsBuilder<RedisStorageOptions>>? configureOptionsBuilder)
        {
            return builder.AddRedisGrainStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptionsBuilder);
        }

        /// <summary>
        /// 将 Redis 配置为 Grain 存储提供程序。
        /// </summary>
        public static ISiloBuilder AddRedisGrainStorage(this ISiloBuilder builder, string name)
            => builder.AddRedisGrainStorage(name, configureOptionsBuilder: null);

        /// <summary>
        /// 将 Redis 配置为 Grain 存储提供程序。
        /// </summary>
        public static ISiloBuilder AddRedisGrainStorage(this ISiloBuilder builder, string name, Action<OptionsBuilder<RedisStorageOptions>>? configureOptionsBuilder)
        {
            return builder.ConfigureServices(services => services.AddRedisGrainStorage(name, configureOptionsBuilder));
        }
    }
}
