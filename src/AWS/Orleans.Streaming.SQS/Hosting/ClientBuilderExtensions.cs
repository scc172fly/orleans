using System;
using Orleans.Configuration;

namespace Orleans.Hosting
{
    /// <summary>
    /// 为配置 Orleans 客户的亚马逊 SQS 支持的持久性流提供扩展方法。
    /// </summary>
    public static class ClientBuilderExtensions
    {
        /// <summary>
        /// 配置客户端使用 Amazon SQS 支持的持久性流提供者。
        /// </summary>
        /// <param name="builder">客户端构建器。</param>
        /// <param name="name">流体提供者的名称。</param>
        /// <param name="configureOptions">用于配置 SQS 选项的代表。</param>
        /// <returns>客户端构建器。</returns>
        public static IClientBuilder AddSqsStreams(this IClientBuilder builder, string name, Action<SqsOptions> configureOptions)
        {
            builder.AddSqsStreams(name, b =>
                b.ConfigureSqs(ob => ob.Configure(configureOptions)));
            return builder;
        }

        /// <summary>
        /// 配置客户端使用 Amazon SQS 支持的持久性流提供者。
        /// </summary>
        /// <param name="builder">客户端构建器。</param>
        /// <param name="name">流体提供者的名称。</param>
        /// <param name="configure">用于配置流提供方的代表。</param>
        /// <returns>客户端构建器。</returns>
        public static IClientBuilder AddSqsStreams(this IClientBuilder builder, string name, Action<ClusterClientSqsStreamConfigurator> configure)
        {
            var configurator = new ClusterClientSqsStreamConfigurator(name, builder);
            configure?.Invoke(configurator);
            return builder;
        }
    }
}
