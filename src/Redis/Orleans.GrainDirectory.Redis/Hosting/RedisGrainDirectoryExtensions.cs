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
    /// 配置 Redis 作为 grain 目录提供者的扩展名。
    /// </summary>
    public static class RedisGrainDirectoryExtensions
    {
        /// <summary>
        /// 添加一个默认 grain 目录,该目录在 Redis中存在持续条目。
        /// </summary>
        /// <param name="builder">下 silo 建筑商</param>
        /// <param name="configureOptions">用于配置提供者的代表。</param>
        /// <returns>下 silo 建筑商</returns>
        public static ISiloBuilder UseRedisGrainDirectoryAsDefault(
            this ISiloBuilder builder,
            Action<RedisGrainDirectoryOptions> configureOptions)
        {
            return builder.UseRedisGrainDirectoryAsDefault(ob => ob.Configure(configureOptions));
        }

        /// <summary>
        /// 添加一个默认 grain 目录,该目录在 Redis中存在持续条目。
        /// </summary>
        /// <param name="builder">下 silo 建筑商</param>
        /// <param name="configureOptions">用于配置提供者选项构建器的代表。</param>
        /// <returns>下 silo 建筑商</returns>
        public static ISiloBuilder UseRedisGrainDirectoryAsDefault(
            this ISiloBuilder builder,
            Action<OptionsBuilder<RedisGrainDirectoryOptions>> configureOptions)
        {
            return builder.ConfigureServices(services => services.AddRedisGrainDirectory(GrainDirectoryAttribute.DEFAULT_GRAIN_DIRECTORY, configureOptions));
        }

        /// <summary>
        /// 添加命名 grain 持续在 Redis.
        /// </summary>
        /// <param name="builder">下 silo 建筑商</param>
        /// <param name="name"> grain 目录的名称。</param>
        /// <param name="configureOptions">用于配置提供者的代表。</param>
        /// <returns>下 silo 建筑商</returns>
        public static ISiloBuilder AddRedisGrainDirectory(
            this ISiloBuilder builder,
            string name,
            Action<RedisGrainDirectoryOptions> configureOptions)
        {
            return builder.AddRedisGrainDirectory(name, ob => ob.Configure(configureOptions));
        }

        /// <summary>
        /// 添加命名 grain 持续在 Redis.
        /// </summary>
        /// <param name="builder">下 silo 建筑商</param>
        /// <param name="name"> grain 目录的名称。</param>
        /// <param name="configureOptions">用于配置提供者选项构建器的代表。</param>
        /// <returns>下 silo 建筑商</returns>
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
