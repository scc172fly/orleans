
using System;
using Orleans.Providers.Streams.Generator;

namespace Orleans.Hosting
{
    /// <summary>
    /// 简单生成器的配置类。
    /// 此类用于配置生成器流提供程序，使其使用 SimpleGenerator 生成流，
    /// </summary>
    [Serializable]
    [GenerateSerializer]
    public sealed class SimpleGeneratorOptions : IStreamGeneratorConfig
    {
        /// <summary>
        /// 获取或设置流命名空间。
        /// </summary>
        /// <value>流命名空间。</value>
        [Id(0)]
        public string StreamNamespace { get; set; } = null!; // 在使用之前由配置或对象初始化器设置。

        /// <summary>
        /// 获取流生成器类型。
        /// </summary>
        /// <value>流生成器类型。</value>
        public Type StreamGeneratorType => typeof(SimpleGenerator);

        /// <summary>
        /// 获取或设置要生成的事件数量。
        /// </summary>
        /// <value>要生成的事件数量。</value>
        [Id(1)]
        public int EventsInStream { get; set; } = DEFAULT_EVENTS_IN_STREAM;

        /// <summary>
        /// 默认生成的事件数量。
        /// </summary>
        public const int DEFAULT_EVENTS_IN_STREAM = 100;
    }
}
