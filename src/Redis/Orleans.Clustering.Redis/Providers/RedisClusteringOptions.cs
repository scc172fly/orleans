using Microsoft.Extensions.Options;
using Orleans.Runtime;
using StackExchange.Redis;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Threading.Tasks;
using Orleans.Configuration;

namespace Orleans.Clustering.Redis
{
    /// <summary>
    /// Redis 集群成员管理选项。
    /// </summary>
    public class RedisClusteringOptions
    {
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
        public Func<RedisClusteringOptions, Task<(IConnectionMultiplexer Multiplexer, bool IsShared)>> CreateMultiplexer { get; set; } = DefaultCreateMultiplexer;

        /// <summary>
        /// 用于为 RedisMembershipTable 创建 Redis 键的委托。
        /// </summary>
        public Func<ClusterOptions, RedisKey> CreateRedisKey { get; set; } = DefaultCreateRedisKey;

        /// <summary>
        /// 获取或设置集群成员资格哈希表的过期时间；初始化表版本时会刷新该过期时间。
        /// 默认值为 <see langword="null"/>。对于测试等临时环境，可配置过期时间。
        /// 过期会删除表版本及所有成员；此后读取成员资格时会报告表不存在。
        /// </summary>
        public TimeSpan? EntryExpiry { get; set; } = null;

        /// <summary>
        /// 默认连接多路复用器创建委托。
        /// </summary>
        /// <param name="options">Redis 集群成员管理选项。</param>
        /// <returns>包含已创建的连接多路复用器及其是否共享的标志的任务。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> 为 <see langword="null"/>。</exception>
        public static async Task<(IConnectionMultiplexer Multiplexer, bool IsShared)> DefaultCreateMultiplexer(RedisClusteringOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            return (await ConnectionMultiplexer.ConnectAsync(options.ConfigurationOptions!), false);
        }

        /// <summary>
        /// 为 <see cref="RedisMembershipTable"/> 创建默认 Redis 键。
        /// </summary>
        /// <param name="clusterOptions">集群标识选项。</param>
        /// <returns>Redis 成员表键。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="clusterOptions"/> 为 <see langword="null"/>。</exception>
        public static RedisKey DefaultCreateRedisKey(ClusterOptions clusterOptions)
        {
            ArgumentNullException.ThrowIfNull(clusterOptions);
            return Encoding.UTF8.GetBytes($"{clusterOptions.ServiceId}/members/{clusterOptions.ClusterId}");
        }
    }

    internal class RedactRedisConfigurationOptions : RedactAttribute
    {
        public override string Redact(object? value) => value is ConfigurationOptions cfg ? cfg.ToString(includePassword: false) : base.Redact(value);
    }

    /// <summary>
    /// <see cref="RedisClusteringOptions"/> 的配置验证器。
    /// </summary>
    public class RedisClusteringOptionsValidator : IConfigurationValidator
    {
        private readonly RedisClusteringOptions _options;

        /// <summary>
        /// 初始化 <see cref="RedisClusteringOptionsValidator"/> 类的新实例。
        /// </summary>
        /// <param name="options">要验证的选项。</param>
        [SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "Microsoft.Extensions.DependencyInjection supplies the resolved options instance.")]
        public RedisClusteringOptionsValidator(IOptions<RedisClusteringOptions> options)
        {
            _options = options.Value;
        }

        /// <inheritdoc/>
        public void ValidateConfiguration()
        {
            if (_options.ConfigurationOptions == null)
            {
                throw new OrleansConfigurationException($"Invalid configuration for {nameof(RedisMembershipTable)}. {nameof(RedisClusteringOptions)}.{nameof(_options.ConfigurationOptions)} is required.");
            }
        }
    }
}
