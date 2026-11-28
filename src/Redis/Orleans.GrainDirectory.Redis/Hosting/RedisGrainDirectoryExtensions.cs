using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.GrainDirectory;
using Orleans.GrainDirectory.Redis;
using Orleans.Runtime;
using Orleans.Runtime.Hosting;

namespace Orleans.Hosting
{
    /// <summary>
    /// 用于将 Redis 配置为 Grain 目录提供程序的扩展方法。
    /// </summary>
    public static class RedisGrainDirectoryExtensions
    {
        /// <summary>
        /// 添加将目录条目持久化到 Redis 的默认 Grain 目录。
        /// </summary>
        /// <param name="builder">Silo 构建器。</param>
        /// <param name="configureOptions">用于配置提供程序的委托。</param>
        /// <returns>Silo 构建器。</returns>
        public static ISiloBuilder UseRedisGrainDirectoryAsDefault(
            this ISiloBuilder builder,
            Action<RedisGrainDirectoryOptions> configureOptions)
        {
            return builder.UseRedisGrainDirectoryAsDefault(ob => ob.Configure(configureOptions));
        }

        /// <summary>
        /// 添加将目录条目持久化到 Redis 的默认 Grain 目录。
        /// </summary>
        /// <param name="builder">Silo 构建器。</param>
        /// <param name="configureOptions">用于配置提供程序选项构建器的委托。</param>
        /// <returns>Silo 构建器。</returns>
        public static ISiloBuilder UseRedisGrainDirectoryAsDefault(
            this ISiloBuilder builder,
            Action<OptionsBuilder<RedisGrainDirectoryOptions>> configureOptions)
        {
            return builder.ConfigureServices(services => services.AddRedisGrainDirectory(GrainDirectoryAttribute.DEFAULT_GRAIN_DIRECTORY, configureOptions));
        }

        /// <summary>
        /// 添加将目录条目持久化到 Redis 的命名 Grain 目录。
        /// </summary>
        /// <param name="builder">Silo 构建器。</param>
        /// <param name="name">Grain 目录名称。</param>
        /// <param name="configureOptions">用于配置提供程序的委托。</param>
        /// <returns>Silo 构建器。</returns>
        public static ISiloBuilder AddRedisGrainDirectory(
            this ISiloBuilder builder,
            string name,
            Action<RedisGrainDirectoryOptions> configureOptions)
        {
            return builder.AddRedisGrainDirectory(name, ob => ob.Configure(configureOptions));
        }

        /// <summary>
        /// 添加将目录条目持久化到 Redis 的命名 Grain 目录。
        /// </summary>
        /// <param name="builder">Silo 构建器。</param>
        /// <param name="name">Grain 目录名称。</param>
        /// <param name="configureOptions">用于配置提供程序选项构建器的委托。</param>
        /// <returns>Silo 构建器。</returns>
        public static ISiloBuilder AddRedisGrainDirectory(
            this ISiloBuilder builder,
            string name,
            Action<OptionsBuilder<RedisGrainDirectoryOptions>> configureOptions)
        {
            return builder.ConfigureServices(services => services.AddRedisGrainDirectory(name, configureOptions));
        }

        private static IServiceCollection AddRedisGrainDirectory(
            this IServiceCollection services,
            string name,
            Action<OptionsBuilder<RedisGrainDirectoryOptions>> configureOptions)
        {
            configureOptions.Invoke(services.AddOptions<RedisGrainDirectoryOptions>(name));
            services
                .AddTransient<IConfigurationValidator>(sp => new RedisGrainDirectoryOptionsValidator(sp.GetRequiredService<IOptionsMonitor<RedisGrainDirectoryOptions>>().Get(name), name))
                .ConfigureNamedOptionForLogging<RedisGrainDirectoryOptions>(name)
                .AddGrainDirectory(name, (sp, key) => ActivatorUtilities.CreateInstance<RedisGrainDirectory>(sp, sp.GetOptionsByName<RedisGrainDirectoryOptions>(key)));

            return services;
        }
    }
}
