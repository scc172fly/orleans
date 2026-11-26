
using System;
using Orleans.Providers.Streams.Generator;

namespace Orleans.Hosting
{
    /// <summary>
    /// 简单的发电机配置类。
    /// 此类用于配置发电机流提供商, 以使用 SimpleGenerator生成流
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
        public string StreamNamespace { get; set; } = null!; // 使用前由配置/对象初始化器设置 。

        /// <summary>
        /// 获取流源生成器类型
        /// </summary>
        /// <value>流体生成器的类型。</value>
        public Type StreamGeneratorType => typeof(SimpleGenerator);

        /// <summary>
        /// 获取或设置要生成的事件数量 。
        /// </summary>
        /// <value>要生成的事件数量。</value>
        [Id(1)]
        public int EventsInStream { get; set; } = DEFAULT_EVENTS_IN_STREAM;

        /// <summary>
        /// 要生成的事件的默认数量 。
        /// </summary>
        public const int DEFAULT_EVENTS_IN_STREAM = 100;
    }
}
