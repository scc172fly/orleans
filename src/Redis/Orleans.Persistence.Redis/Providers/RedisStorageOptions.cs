using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Runtime;
using Orleans.Storage;
using StackExchange.Redis;

namespace Orleans.Persistence
{
    /// <summary>
    /// Redis Grain 存储选项。
    /// </summary>
    public class RedisStorageOptions : IStorageProviderSerializerOptions
    {
        /// <summary>
        /// 是否在清除操作期间删除状态。
        /// </summary>
        public bool DeleteStateOnClear { get; set; }

        /// <summary>
        /// 应初始化存储的 Silo 生命周期阶段。存储必须在使用前完成初始化。
        /// </summary>
        public int InitStage { get; set; } = ServiceLifecycleStage.ApplicationServices;

        /// <inheritdoc/>
        public IGrainStorageSerializer GrainStorageSerializer { get; set; } = default!;

        /// <summary>
        /// 获取或设置 Redis 客户端配置。
        /// </summary>
        [RedactRedisConfigurationOptions]
        public ConfigurationOptions? ConfigurationOptions { get; set; }

        /// <summary>
        /// 用于创建 Redis 连接多路复用器并指明其是否共享的委托。
        /// </summary>
        /// <remarks>
        /// 当 <c>IsShared</c> 为 <see langword="true"/> 时，提供程序不会释放返回的连接多路复用器。
        /// </remarks>
        public Func<RedisStorageOptions, Task<(IConnectionMultiplexer Multiplexer, bool IsShared)>> CreateMultiplexer { get; set; } = DefaultCreateMultiplexer;

        /// <summary>
        /// 条目过期时间，默认为 null。仅应在测试环境等临时环境中设置此值。
        /// 将其设置为非 <see langword="null"/> 值会导致集群中出现重复激活实例。
        /// </summary>
        public TimeSpan? EntryExpiry { get; set; } = null;

        /// <summary>
        /// 获取指定 Grain 类型及 Grain 标识符对应的 Redis 键。如果未设置，则使用默认实现，其等效于 <c>{ServiceId}/state/{grainId}/{grainType}</c>。
        /// </summary>
        public Func<string, GrainId, RedisKey>? GetStorageKey { get; set; }

        /// <summary>
        /// 默认连接多路复用器创建委托。
        /// </summary>
        /// <param name="options">包含 Redis 连接配置的存储选项。</param>
        /// <returns>包含已创建的连接多路复用器以及该复用器由提供程序拥有的标志的任务。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> 为 <see langword="null"/>。</exception>
        public static async Task<(IConnectionMultiplexer Multiplexer, bool IsShared)> DefaultCreateMultiplexer(RedisStorageOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            return (Multiplexer: await ConnectionMultiplexer.ConnectAsync(options.ConfigurationOptions!), IsShared: false);
        }
    }

    /// <summary>
    /// 用于配置 <see cref="RedisStorageOptions"/> 的扩展方法。
    /// </summary>
    public static class RedisStorageOptionsExtensions
    {
        /// <summary>
        /// 配置给定选项以使用忽略 Grain 类型的 Redis 键格式，其等效于 <c>{ServiceId}/state/{grainId}</c>。
        /// </summary>
        /// <remarks>
        /// 此方法为从 Redis 存储提供程序预发行版本迁移的用户提供兼容性工具。
        /// </remarks>
        /// <param name="optionsBuilder">选项构建器。</param>
        /// <exception cref="ArgumentNullException"><paramref name="optionsBuilder"/> 为 <see langword="null"/>。</exception>
        public static void UseGetRedisKeyIgnoringGrainType(this OptionsBuilder<RedisStorageOptions> optionsBuilder)
        {
            ArgumentNullException.ThrowIfNull(optionsBuilder);

            optionsBuilder.Configure((RedisStorageOptions options, IOptions<ClusterOptions> clusterOptions) =>
            {
                RedisKey keyPrefix = Encoding.UTF8.GetBytes($"{clusterOptions.Value.ServiceId}/state/");
                options.GetStorageKey = (_, grainId) => keyPrefix.Append(grainId.ToString());
            });
        }
    }

    internal class RedactRedisConfigurationOptions : RedactAttribute
    {
        public override string Redact(object? value) => value is ConfigurationOptions cfg ? cfg.ToString(includePassword: false) : base.Redact(value);
    }
}
