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
    /// Redis 组合的选项。
    /// </summary>
    public class RedisClusteringOptions
    {
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
        public Func<RedisClusteringOptions, Task<(IConnectionMultiplexer Multiplexer, bool IsShared)>> CreateMultiplexer { get; set; } = DefaultCreateMultiplexer;

        /// <summary>
        /// 该代表用来为 RedisMembershipTable创建复盘键。
        /// </summary>
        public Func<ClusterOptions, RedisKey> CreateRedisKey { get; set; } = DefaultCreateRedisKey;

        /// <summary>
        /// 获取或设定该小组成员的散列,在初始化表格版本时刷新。
        /// 默认值为 <see langword="null"/>。配置短时间环境的过期,例如测试。
        /// 过期失效取消表格版本和每个成员;随后的成员为缺表报告。
        /// </summary>
        public TimeSpan? EntryExpiry { get; set; } = null;

        /// <summary>
        /// 默认的多路驱动器创建代表 。
        /// </summary>
        /// <param name="options">下 Redis 组合组合选项。</param>
        /// <returns>任务包含创建的多路驱动器,并含有一个数值,表明是否共享。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> 是 <see langword="null"/>.</exception>
        public static async Task<(IConnectionMultiplexer Multiplexer, bool IsShared)> DefaultCreateMultiplexer(RedisClusteringOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            return (await ConnectionMultiplexer.ConnectAsync(options.ConfigurationOptions!), false);
        }

        /// <summary>
        /// 创建默认创建默认 Redis 键的密钥用 <see cref="RedisMembershipTable"/>.
        /// </summary>
        /// <param name="clusterOptions">集群识别选项。</param>
        /// <returns>下 Redis 成员表格键。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="clusterOptions"/> 是 <see langword="null"/>.</exception>
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
    /// <see cref="RedisClusteringOptions"/>的配置验证符 。
    /// </summary>
    public class RedisClusteringOptionsValidator : IConfigurationValidator
    {
        private readonly RedisClusteringOptions _options;

        /// <summary>
        /// 初始化 <see cref="RedisClusteringOptionsValidator"/> 类的新实例。
        /// </summary>
        /// <param name="options">验证的选项。</param>
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
