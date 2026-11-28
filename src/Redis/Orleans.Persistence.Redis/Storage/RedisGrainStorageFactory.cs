using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Storage;
using System;

namespace Orleans.Persistence
{
    /// <summary>
    /// 用于创建 Redis Grain 存储实例的工厂。
    /// </summary>
    public static class RedisGrainStorageFactory
    {
        /// <summary>
        /// 创建 Grain 存储实例。
        /// </summary>
        public static RedisGrainStorage Create(IServiceProvider services, string name)
        {
            var optionsMonitor = services.GetRequiredService<IOptionsMonitor<RedisStorageOptions>>();
            var redisGrainStorage = ActivatorUtilities.CreateInstance<RedisGrainStorage>(services, name, optionsMonitor.Get(name));
            return redisGrainStorage;
        }
    }
}
