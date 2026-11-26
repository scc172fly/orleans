using System;
using System.Threading.Tasks;
using Orleans.GrainDirectory.Redis;
using Orleans.Runtime;
using StackExchange.Redis;

namespace Orleans.Configuration
{
    /// <summary>
    /// <see cref="RedisGrainDirectory"/>配置选项
    /// </summary>
    public class RedisGrainDirectoryOptions
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
        public Func<RedisGrainDirectoryOptions, Task<(IConnectionMultiplexer Multiplexer, bool IsShared)>> CreateMultiplexer { get; set; } = DefaultCreateMultiplexer;

        /// <summary>
        /// 输入过期, 默认为无效。 值应该只为短时间环境设定( 如测试中) 。
        /// 设定一个不同于无效的值将导致集群中重复激活。
        /// </summary>
        public TimeSpan? EntryExpiry { get; set; } = null;

        /// <summary>
        /// 默认的多路驱动器创建代表 。
        /// </summary>
        /// <param name="options"> Redis grain 目录选项。</param>
        /// <returns>返回一个自供应商拥有的连接多路驱动器的任务。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> 是 <see langword="null"/>.</exception>
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
    /// <see cref="RedisGrainDirectoryOptions"/>的配置验证符 。
    /// </summary>
    public class RedisGrainDirectoryOptionsValidator : IConfigurationValidator
    {
        private readonly RedisGrainDirectoryOptions _options;
        private readonly string _name;

        /// <summary>
        /// 初始化 <see cref="RedisGrainDirectoryOptionsValidator"/> 类的新实例。
        /// </summary>
        /// <param name="options">验证的选项。</param>
        /// <param name="name"> grain 目录的名称。</param>
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
