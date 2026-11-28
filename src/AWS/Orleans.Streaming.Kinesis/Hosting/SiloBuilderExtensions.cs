using System;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Streaming.Kinesis;
using Orleans.Streams;

namespace Orleans.Hosting
{
    /// <summary>
    /// 提供用于在 Orleans Silo 上配置 Amazon Kinesis Data Streams 持久化流的扩展方法。
    /// </summary>
    public static class SiloBuilderExtensions
    {
        /// <summary>
        /// 配置 Silo 使用由 Amazon Kinesis Data Streams 支持的持久化流提供程序。
        /// </summary>
        /// <remarks>
        /// 此重载使用基于 Orleans Grain 的检查点记录器。检查点通过
        /// <c>PubSubStore</c> Grain 存储提供程序持久化，无需 DynamoDB 或 Kinesis Client Library。
        /// </remarks>
        /// <param name="builder">Silo 构建器。</param>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="configureOptions">用于配置 Kinesis 选项的委托。</param>
        /// <returns>Silo 构建器。</returns>
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
        /// 配置 Silo 使用由 Amazon Kinesis Data Streams 支持的持久化流提供程序。
        /// </summary>
        /// <param name="builder">Silo 构建器。</param>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="configure">用于配置流提供程序的委托。</param>
        /// <returns>Silo 构建器。</returns>
        public static ISiloBuilder AddKinesisStreams(this ISiloBuilder builder, string name, Action<SiloKinesisStreamConfigurator> configure)
        {
            var configurator = new SiloKinesisStreamConfigurator(name,
                configureServicesDelegate => builder.ConfigureServices(configureServicesDelegate));
            configure?.Invoke(configurator);
            return builder;
        }
    }
}
