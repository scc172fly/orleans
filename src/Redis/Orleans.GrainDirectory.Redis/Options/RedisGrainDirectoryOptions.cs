using System;
using System.Threading.Tasks;
using Orleans.GrainDirectory.Redis;
using Orleans.Runtime;
using StackExchange.Redis;

namespace Orleans.Configuration
{
    /// <summary>
    /// <see cref="RedisGrainDirectory"/> 的配置选项。
    /// </summary>
    public class RedisGrainDirectoryOptions
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
        public Func<RedisGrainDirectoryOptions, Task<(IConnectionMultiplexer Multiplexer, bool IsShared)>> CreateMultiplexer { get; set; } = DefaultCreateMultiplexer;

        /// <summary>
        /// 条目过期时间，默认为 null。仅应在临时环境（例如测试）中设置此值。
        /// 将其设置为非 null 值会导致集群中出现重复激活实例。
        /// </summary>
        public TimeSpan? EntryExpiry { get; set; } = null;

        /// <summary>
        /// 默认连接多路复用器创建委托。
        /// </summary>
        /// <param name="options">Redis Grain 目录选项。</param>
        /// <returns>返回由提供程序拥有的连接多路复用器的任务。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> 为 <see langword="null"/>。</exception>
        public static async Task<(IConnectionMultiplexer Multiplexer, bool IsShared)> DefaultCreateMultiplexer(RedisGrainDirectoryOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            return (Multiplexer: await ConnectionMultiplexer.ConnectAsync(options.ConfigurationOptions!), IsShared: false);
        }
    }

    internal class RedactRedisConfigurationOptions : RedactAttribute
    {
        public override string Redact(object? value) => value is ConfigurationOptions cfg ? cfg.ToString(includePassword: false) : base.Redact(value);
    }

    /// <summary>
    /// <see cref="RedisGrainDirectoryOptions"/> 的配置验证器。
    /// </summary>
    public class RedisGrainDirectoryOptionsValidator : IConfigurationValidator
    {
        private readonly RedisGrainDirectoryOptions _options;
        private readonly string _name;

        /// <summary>
        /// 初始化 <see cref="RedisGrainDirectoryOptionsValidator"/> 类的新实例。
        /// </summary>
        /// <param name="options">要验证的选项。</param>
        /// <param name="name">Grain 目录名称。</param>
        public RedisGrainDirectoryOptionsValidator(RedisGrainDirectoryOptions options, string name)
        {
            _options = options;
            _name = name;
        }

        /// <inheritdoc/>
        public void ValidateConfiguration()
        {
            if (_options.ConfigurationOptions == null)
            {
                throw new OrleansConfigurationException($"Invalid configuration for {nameof(RedisGrainDirectory)} with name {_name}. {nameof(RedisGrainDirectoryOptions)}.{nameof(_options.ConfigurationOptions)} is required.");
            }
        }
    }
}
