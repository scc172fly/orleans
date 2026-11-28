using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;
using Orleans.Configuration;

namespace Orleans.Hosting
{
    /// <summary>
    /// 用于在 <see cref="IClientBuilder"/> 上配置 Azure Queue 流提供程序的扩展方法。
    /// </summary>
    public static class ClientBuilderExtensions
    {
        /// <summary>
        /// 配置集群客户端使用 Azure Queue 持久化流。
        /// </summary>
        public static IClientBuilder AddAzureQueueStreams(this IClientBuilder builder,
            string name,
            Action<ClusterClientAzureQueueStreamConfigurator> configure)
        {
            // 构造函数将 AzureQueueStream 接入 DI，因此无论 configure 是否为 null，都必须调用它。
            var configurator = new ClusterClientAzureQueueStreamConfigurator(name, builder);
            configure?.Invoke(configurator);
            return builder;
        }

        /// <summary>
        /// 配置集群客户端使用 Azure Queue 持久化流。
        /// </summary>
        public static IClientBuilder AddAzureQueueStreams(this IClientBuilder builder,
            string name, Action<OptionsBuilder<AzureQueueOptions>> configureOptions)
        {
            builder.AddAzureQueueStreams(name, b => b.ConfigureAzureQueue(configureOptions));
            return builder;
        }

        /// <summary>
        /// 配置集群客户端使用采用 JSON 序列化的 Azure Queue 持久化流。
        /// 此功能处于实验阶段，未来更新中可能发生变化。
        /// </summary>
        /// <param name="builder">客户端构建器。</param>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="configure">启用 JSON 的 Azure Queue 流提供程序的配置委托。</param>
        /// <returns>用于链式调用的客户端构建器。</returns>
        [Experimental("StreamingJsonSerializationExperimental", UrlFormat = "https://github.com/dotnet/orleans/pull/9618")]
        public static IClientBuilder AddAzureQueueJsonStreams(this IClientBuilder builder,
            string name,
            Action<ClusterClientAzureQueueJsonStreamConfigurator> configure)
        {
            var configurator = new ClusterClientAzureQueueJsonStreamConfigurator(name, builder);
            configure?.Invoke(configurator);
            return builder;
        }

        /// <summary>
        /// 配置集群客户端使用采用 JSON 序列化和默认设置的 Azure Queue 持久化流。
        /// 此功能处于实验阶段，未来更新中可能发生变化。
        /// </summary>
        /// <param name="builder">客户端构建器。</param>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="configureOptions">Azure Queue 选项的配置委托。</param>
        /// <returns>用于链式调用的客户端构建器。</returns>
        [Experimental("StreamingJsonSerializationExperimental", UrlFormat = "https://github.com/dotnet/orleans/pull/9618")]
        public static IClientBuilder AddAzureQueueJsonStreams(this IClientBuilder builder,
            string name, Action<OptionsBuilder<AzureQueueOptions>> configureOptions)
        {
            builder.AddAzureQueueJsonStreams(name, b => b.ConfigureAzureQueue(configureOptions));
            return builder;
        }
    }
}
