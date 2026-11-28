using System;
using Orleans.Streaming.Kinesis;

namespace Orleans.Hosting
{
    /// <summary>
    /// 提供用于在 Orleans 客户端上配置 Amazon Kinesis Data Streams 持久化流的扩展方法。
    /// </summary>
    public static class ClientBuilderExtensions
    {
        /// <summary>
        /// 配置客户端使用由 Amazon Kinesis Data Streams 支持的持久化流提供程序。
        /// </summary>
        /// <param name="builder">客户端构建器。</param>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="configureOptions">用于配置 Kinesis 选项的委托。</param>
        /// <returns>客户端构建器。</returns>
        public static IClientBuilder AddKinesisStreams(this IClientBuilder builder, string name, Action<KinesisStreamOptions> configureOptions)
        {
            builder.AddKinesisStreams(name, b =>
                b.ConfigureKinesis(ob => ob.Configure(configureOptions)));
            return builder;
        }

        /// <summary>
        /// 配置客户端使用由 Amazon Kinesis Data Streams 支持的持久化流提供程序。
        /// </summary>
        /// <param name="builder">客户端构建器。</param>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="configure">用于配置流提供程序的委托。</param>
        /// <returns>客户端构建器。</returns>
        public static IClientBuilder AddKinesisStreams(this IClientBuilder builder, string name, Action<ClusterClientKinesisStreamConfigurator> configure)
        {
            var configurator = new ClusterClientKinesisStreamConfigurator(name, builder);
            configure?.Invoke(configurator);
            return builder;
        }
    }
}
