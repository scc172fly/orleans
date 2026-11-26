using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Storage;
using System;

namespace Orleans.Persistence
{
    /// <summary>
    /// 用于创建 Redis grain 储存实例的工厂。
    /// </summary>
    public static class RedisGrainStorageFactory
    {
        /// <summary>
        /// 创建 grain 存储实例。
        /// </summary>
        public static RedisGrainStorage Create(IServiceProvider services, string name)
        {
            var optionsMonitor = services.GetRequiredService<IOptionsMonitor<RedisStorageOptions>>();
            var redisGrainStorage = ActivatorUtilities.CreateInstance<RedisGrainStorage>(services, name, optionsMonitor.Get(name));
            return redisGrainStorage;
        }
    }
}
