using System;
using Orleans.Streaming.Kinesis;

namespace Orleans.Hosting
{
    /// <summary>
    /// 提供用于配置 Orleans 客户的亚马逊 Kinesis数据流支持的持续流的扩展方法。
    /// </summary>
    public static class ClientBuilderExtensions
    {
        /// <summary>
        /// 配置客户端使用亚马逊 Kinesis 数据流支持的持久性流提供商 。
        /// </summary>
        /// <param name="builder">客户端构建器。</param>
        /// <param name="name">流体提供者的名称。</param>
        /// <param name="configureOptions">代表用于配置 Kinesis 选项。</param>
        /// <returns>客户端构建器。</returns>
        public static IClientBuilder AddKinesisStreams(this IClientBuilder builder, string name, Action<KinesisStreamOptions> configureOptions)
        {
            builder.AddKinesisStreams(name, b =>
                b.ConfigureKinesis(ob => ob.Configure(configureOptions)));
            return builder;
        }

        /// <summary>
        /// 配置客户端使用亚马逊 Kinesis 数据流支持的持久性流提供商 。
        /// </summary>
        /// <param name="builder">客户端构建器。</param>
        /// <param name="name">流体提供者的名称。</param>
        /// <param name="configure">用于配置流提供方的代表。</param>
        /// <returns>客户端构建器。</returns>
        public static IClientBuilder AddKinesisStreams(this IClientBuilder builder, string name, Action<ClusterClientKinesisStreamConfigurator> configure)
        {
            var configurator = new ClusterClientKinesisStreamConfigurator(name, builder);
            configure?.Invoke(configurator);
            return builder;
        }
    }
}
