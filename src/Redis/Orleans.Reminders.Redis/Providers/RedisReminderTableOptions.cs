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
    /// Redis 提醒器选项。
    /// </summary>
    public class RedisReminderTableOptions
    {
        /// <summary>
        /// 获取或设置 Redis 客户端选项。
        /// </summary>
        [RedactRedisConfigurationOptions]
        public ConfigurationOptions? ConfigurationOptions { get; set; }

        /// <summary>
        /// 用于创建 Redis 连接多路复用器并指明其是否共享的委托。
        /// </summary>
        /// <remarks>
        /// 当 <c>IsShared</c> 为 <see langword="true"/> 时，提供程序不会释放返回的连接多路复用器。
        /// </remarks>
        public Func<RedisReminderTableOptions, Task<(IConnectionMultiplexer Multiplexer, bool IsShared)>> CreateMultiplexer { get; set; } = DefaultCreateMultiplexer;

        /// <summary>
        /// 条目过期时间，默认为 null。仅应在临时环境（例如测试）中设置此值。
        /// 将其设置为非 null 值会导致提醒器条目在一段时间后被删除。
        /// </summary>
        public TimeSpan? EntryExpiry { get; set; } = null;

        /// <summary>
        /// 默认连接多路复用器创建委托。
        /// </summary>
        /// <param name="options">包含 Redis 连接配置的提醒器表选项。</param>
        /// <returns>包含已创建的连接多路复用器以及该复用器由提供程序拥有的标志的任务。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="options"/> 为 <see langword="null"/>。</exception>
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
    /// <see cref="RedisReminderTableOptions"/> 的配置验证器。
    /// </summary>
    public class RedisReminderTableOptionsValidator : IConfigurationValidator
    {
        private readonly RedisReminderTableOptions _options;

        /// <summary>
        /// 初始化 <see cref="RedisReminderTableOptionsValidator"/> 类的新实例。
        /// </summary>
        /// <param name="options">要验证的提醒器表选项。</param>
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
