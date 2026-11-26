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
    /// Redis grain 储存选项。
    /// </summary>
    public class RedisStorageOptions : IStorageProviderSerializerOptions
    {
        /// <summary>
        /// 是否在明确操作中删除状态 。
        /// </summary>
        public bool DeleteStateOnClear { get; set; }

        /// <summary>
        /// 储存应初始化的 silo 生命周期阶段,储存必须在使用前初始化。
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
        /// 该代表用来创建 Redis 连接多路驱动器,并表明该多路转换器是否共享。
        /// </summary>
        /// <remarks>
        /// 当 <c>IsShared</c> 是 <see langword="true"/>时,供应商将不处置返回的多路驱动器。
        /// </remarks>
        public Func<RedisStorageOptions, Task<(IConnectionMultiplexer Multiplexer, bool IsShared)>> CreateMultiplexer { get; set; } = DefaultCreateMultiplexer;

        /// <summary>
        /// 输入过期, 默认为空号。 值应只设定用于短时间环境的值, 如测试环境 。
        /// 设定一个不同于 <see langword="null"/> 的值将导致组群中重复启动。
        /// </summary>
        public TimeSpan? EntryExpiry { get; set; } = null;

        /// <summary>
        /// 得到 Redis 提供键的密钥 grain 和类型 grain 如果未设置 ident , 将使用默认执行, 这相当于 <c>{ServiceId}/state/{grainId}/{grainType}</c>.
        /// </summary>
        public Func<string, GrainId, RedisKey>? GetStorageKey { get; set; }

        /// <summary>
        /// 默认的多路驱动器创建代表 。
        /// </summary>
        /// <param name="options">含有 Redis 连接配置的存储选项。</param>
        /// <returns>含有创建的多路驱动器的任务,并注明供应商拥有该多路驱动器。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> 是 <see langword="null"/>.</exception>
        public static async Task<(IConnectionMultiplexer Multiplexer, bool IsShared)> DefaultCreateMultiplexer(RedisStorageOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            return (Multiplexer: await ConnectionMultiplexer.ConnectAsync(options.ConfigurationOptions!), IsShared: false);
        }
    }

    /// <summary>
    /// 配置的扩展方法 <see cref="RedisStorageOptions"/>。
    /// </summary>
    public static class RedisStorageOptionsExtensions
    {
        /// <summary>
        /// 配置所提供选项的配置 Redis 忽略 grain 类型,等于 <c>{ServiceId}/state/{grainId}</c>.
        /// </summary>
        /// <remarks>
        /// 这种方法是作为兼容性工具,供从 Redis 储存提供者的预发版本迁移过来的用户使用。
        /// </remarks>
        /// <param name="optionsBuilder">选项构建器。</param>
        /// <exception cref="ArgumentNullException"><paramref name="optionsBuilder"/> 是 <see langword="null"/>.</exception>
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
