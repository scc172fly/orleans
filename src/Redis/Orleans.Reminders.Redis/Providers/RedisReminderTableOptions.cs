using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Orleans.Reminders.Redis;
using Orleans.Runtime;
using StackExchange.Redis;

namespace Orleans.Configuration
{
    /// <summary>
    /// Redis 提醒选项。
    /// </summary>
    public class RedisReminderTableOptions
    {
        /// <summary>
        /// 获取或设置 Redis 客户端选项。
        /// </summary>
        [RedactRedisConfigurationOptions]
        public ConfigurationOptions? ConfigurationOptions { get; set; }

        /// <summary>
        /// 该代表用来创建 Redis 连接多路驱动器,并表明该多路转换器是否共享。
        /// </summary>
        /// <remarks>
        /// 当 <c>IsShared</c> 是 <see langword="true"/>时,供应商将不处置返回的多路驱动器。
        /// </remarks>
        public Func<RedisReminderTableOptions, Task<(IConnectionMultiplexer Multiplexer, bool IsShared)>> CreateMultiplexer { get; set; } = DefaultCreateMultiplexer;

        /// <summary>
        /// 输入过期, 默认为无效。 值应该只为短时间环境设定( 如测试中) 。
        /// 设置一个与 null 不同的值, 会在一段时间后导致删除提醒项 。
        /// </summary>
        public TimeSpan? EntryExpiry { get; set; } = null;

        /// <summary>
        /// 默认的多路驱动器创建代表 。
        /// </summary>
        /// <param name="options">包含 Redis 连接配置的提醒表格选项。</param>
        /// <returns>含有创建的多路驱动器的任务,并注明供应商拥有该多路驱动器。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> 是 <see langword="null"/>.</exception>
        public static async Task<(IConnectionMultiplexer Multiplexer, bool IsShared)> DefaultCreateMultiplexer(RedisReminderTableOptions options)
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
    /// <see cref="RedisReminderTableOptions"/>的配置验证符 。
    /// </summary>
    public class RedisReminderTableOptionsValidator : IConfigurationValidator
    {
        private readonly RedisReminderTableOptions _options;

        /// <summary>
        /// 初始化 <see cref="RedisReminderTableOptionsValidator"/> 类的新实例。
        /// </summary>
        /// <param name="options">提醒表选项进行验证。</param>
        [SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "Microsoft.Extensions.DependencyInjection supplies the registered options instance.")]
        public RedisReminderTableOptionsValidator(IOptions<RedisReminderTableOptions> options)
        {
            _options = options.Value;
        }

        /// <inheritdoc />
        public void ValidateConfiguration()
        {
            if (_options.ConfigurationOptions == null)
            {
                throw new OrleansConfigurationException($"Invalid configuration for {nameof(RedisReminderTable)}. {nameof(RedisReminderTableOptions)}.{nameof(_options.ConfigurationOptions)} is required.");
            }
        }
    }
}
