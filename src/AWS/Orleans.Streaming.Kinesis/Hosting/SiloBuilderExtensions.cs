using System;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Streaming.Kinesis;
using Orleans.Streams;

namespace Orleans.Hosting
{
    /// <summary>
    /// 提供用于配置 Orleans silos上亚马逊 Kinesis数据流支持的持久性流的扩展方法。
    /// </summary>
    public static class SiloBuilderExtensions
    {
        /// <summary>
        /// 配置 silo 使用亚马逊 Kinesis 数据流支持的持久性流提供者。
        /// </summary>
        /// <remarks>
        /// 此超过超载使用 Orleans grain- 基点检查器。
        /// <c>PubSubStore</c> grain 储存提供方,不要求 DynamoDB 或 Kinesis 客户端库。
        /// </remarks>
        /// <param name="builder">下 silo 建筑商</param>
        /// <param name="name">流体提供者的名称。</param>
        /// <param name="configureOptions">代表用于配置 Kinesis 选项。</param>
        /// <returns>下 silo 建筑商</returns>
        public static ISiloBuilder AddKinesisStreams(this ISiloBuilder builder, string name, Action<KinesisStreamOptions> configureOptions)
        {
            builder.AddKinesisStreams(name, b =>
            {
                b.ConfigureKinesis(ob => ob.Configure(configureOptions));
                b.UseGrainCheckpointer(ob => ob.Configure(options =>
                    options.CheckpointComparer = StreamCheckpointComparers.Numeric));
            });

            return builder;
        }

        /// <summary>
        /// 配置 silo 使用亚马逊 Kinesis 数据流支持的持久性流提供者。
        /// </summary>
        /// <param name="builder">下 silo 建筑商</param>
        /// <param name="name">流体提供者的名称。</param>
        /// <param name="configure">用于配置流提供方的代表。</param>
        /// <returns>下 silo 建筑商</returns>
        public static ISiloBuilder AddKinesisStreams(this ISiloBuilder builder, string name, Action<SiloKinesisStreamConfigurator> configure)
        {
            var configurator = new SiloKinesisStreamConfigurator(name,
                configureServicesDelegate => builder.ConfigureServices(configureServicesDelegate));
            configure?.Invoke(configurator);
            return builder;
        }
    }
}
