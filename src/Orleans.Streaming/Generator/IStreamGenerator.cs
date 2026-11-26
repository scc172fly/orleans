
using System;
using System.Collections.Generic;
using Orleans.Streams;

namespace Orleans.Providers.Streams.Generator
{
    /// <summary>
    /// GeneratorStreamProvider所使用的发电机接口。
    ///  必须与 GeneratorStreamProvider使用的该接口相符。
    /// </summary>
    public interface IStreamGenerator
    {
        /// <summary>
        /// 如果发电机配置在当前时间生成任何事件, 尝试获取事件
        /// </summary>
        /// <param name="utcNow">当前UTC 时间。</param>
        /// <param name="maxCount">最多可读的事件数。</param>
        /// <param name="events">事件。</param>
        /// <returns><see langword="true" /> 如果事情被读取, <see langword="false" /> 否则。</returns>
        bool TryReadEvents(DateTime utcNow, int maxCount, out List<IBatchContainer> events);

        /// <summary>
        /// 配置溪流生成器。
        /// </summary>
        /// <param name="serviceProvider">服务供应商。</param>
        /// <param name="generatorConfig">发电机配置。</param>
        void Configure(IServiceProvider serviceProvider, IStreamGeneratorConfig generatorConfig);
    }

    /// <summary>
    /// GeneratorStreamProvider所用发电机配置接口的配置。该接口覆盖
    ///   流源提供者配置生成数据生成器所需的最低限度信息集。
    ///   执行此接口时, 添加所需的任何额外配置信息 。
    /// </summary>
    public interface IStreamGeneratorConfig
    {
        /// <summary>
        /// 获取流源生成器类型
        /// </summary>
        Type StreamGeneratorType { get; }
    }
}
