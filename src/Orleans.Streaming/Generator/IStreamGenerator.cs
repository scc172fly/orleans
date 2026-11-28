
using System;
using System.Collections.Generic;
using Orleans.Streams;

namespace Orleans.Providers.Streams.Generator
{
    /// <summary>
    /// GeneratorStreamProvider 所用生成器的接口。任何生成事件的方法
    /// 必须符合此接口，才能由 GeneratorStreamProvider 使用。
    /// </summary>
    public interface IStreamGenerator
    {
        /// <summary>
        /// 如果生成器配置为当前时刻生成事件，则尝试获取一个事件。
        /// </summary>
        /// <param name="utcNow">当前 UTC 时间。</param>
        /// <param name="maxCount">最多读取的事件数量。</param>
        /// <param name="events">事件集合。</param>
        /// <returns>如果读取到了事件，则为 <see langword="true" />；否则为 <see langword="false" />。</returns>
        bool TryReadEvents(DateTime utcNow, int maxCount, out List<IBatchContainer> events);

        /// <summary>
        /// 配置流生成器。
        /// </summary>
        /// <param name="serviceProvider">服务提供程序。</param>
        /// <param name="generatorConfig">生成器的配置。</param>
        void Configure(IServiceProvider serviceProvider, IStreamGeneratorConfig generatorConfig);
    }

    /// <summary>
    /// GeneratorStreamProvider 所用生成器的配置接口。此接口涵盖
    /// 流提供程序配置生成器以生成数据所需的最小信息集。生成器应
    /// 在此接口的实现中添加所需的其他配置信息。
    /// </summary>
    public interface IStreamGeneratorConfig
    {
        /// <summary>
        /// 获取流生成器类型。
        /// </summary>
        Type StreamGeneratorType { get; }
    }
}
