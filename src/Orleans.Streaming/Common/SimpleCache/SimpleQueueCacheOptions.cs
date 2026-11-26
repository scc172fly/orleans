using System;
using Orleans.Runtime;

namespace Orleans.Configuration
{
    /// <summary>
    /// 简单队列缓存的配置选项 。
    /// </summary>
    public class SimpleQueueCacheOptions
    {
        /// <summary>
        /// 获取或设置缓存大小 。
        /// </summary>
        /// <value>缓存的大小。</value>
        public int CacheSize { get; set; } = DEFAULT_CACHE_SIZE;

        /// <summary>
        /// 默认值 <see cref="CacheSize"/>。
        /// </summary>
        public const int DEFAULT_CACHE_SIZE = 4096;
    }

    /// <summary>
    /// 验证 <see cref="SimpleQueueCacheOptions"/>。
    /// </summary>
    public class SimpleQueueCacheOptionsValidator : IConfigurationValidator
    {
        private readonly SimpleQueueCacheOptions options;
        private readonly string name;

        /// <summary>
        /// 初始化 <see cref="SimpleQueueCacheOptionsValidator"/> 类的新实例。
        /// </summary>
        /// <param name="options">选项。</param>
        /// <param name="name">名称。</param>
        private SimpleQueueCacheOptionsValidator(SimpleQueueCacheOptions options, string name)
        {
            this.options = options;
            this.name = name;
        }

        /// <inheritdoc />
        public void ValidateConfiguration()
        {
            if (options.CacheSize <= 0)
                throw new OrleansConfigurationException($"{nameof(SimpleQueueCacheOptions)} on stream provider {this.name} is invalid. {nameof(SimpleQueueCacheOptions.CacheSize)} must be larger than zero");
        }

        /// <summary>
        /// 创建新的 <see cref="SimpleQueueCacheOptionsValidator"/> 实例。
        /// </summary>
        /// <param name="services">服务。</param>
        /// <param name="name">提供者名称。</param>
        /// <returns>一个新的 <see cref="SimpleQueueCacheOptionsValidator"/> 实例。</returns>
        public static IConfigurationValidator Create(IServiceProvider services, string name)
        {
            SimpleQueueCacheOptions queueCacheOptions = services.GetOptionsByName<SimpleQueueCacheOptions>(name);
            return new SimpleQueueCacheOptionsValidator(queueCacheOptions, name);
        }
    }
}
