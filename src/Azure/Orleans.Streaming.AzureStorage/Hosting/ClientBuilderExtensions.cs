using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Options;
using Orleans.Configuration;

namespace Orleans.Hosting
{
    /// <summary>
    /// 配置 Azure 队列流提供者在 <see cref="IClientBuilder"/>上的扩展方法 。
    /// </summary>
    public static class ClientBuilderExtensions
    {
        /// <summary>
        /// 配置组群集客户端, 使用“ zure” 队列持续流 。
        /// </summary>
        public static IClientBuilder AddAzureQueueStreams(this IClientBuilder builder,
            string name,
            Action<ClusterClientAzureQueueStreamConfigurator> configure)
        {
            //建造者电线将 AzureQueueStream,因此无论配置是否无效,都必须被调用
            var configurator = new ClusterClientAzureQueueStreamConfigurator(name, builder);
            configure?.Invoke(configurator);
            return builder;
        }

        /// <summary>
        /// 配置组群集客户端, 使用“ zure” 队列持续流 。
        /// </summary>
        public static IClientBuilder AddAzureQueueStreams(this IClientBuilder builder,
            string name, Action<OptionsBuilder<AzureQueueOptions>> configureOptions)
        {
            builder.AddAzureQueueStreams(name, b => b.ConfigureAzureQueue(configureOptions));
            return builder;
        }

        /// <summary>
        /// 配置组群集客户端, 使用 Azure 将有 JSON 序列化的持久性流排队 。
        /// 这是试验性的,今后更新时会改变。
        /// </summary>
        /// <param name="builder">客户端构建器。</param>
        /// <param name="name">流体提供者名称。</param>
        /// <param name="configure"> JSON- 启用的 Azure 队列流提供者配置代表。</param>
        /// <returns>用于方法链的客户端构建器。</returns>
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
        /// 配置组客户端, 使用 Azure 的队列持续流, 并有 JSON 序列化和默认设置 。
        /// 这是试验性的,今后更新时会改变。
        /// </summary>
        /// <param name="builder">客户端构建器。</param>
        /// <param name="name">流体提供者名称。</param>
        /// <param name="configureOptions">配置代表 Azure 队列选项。</param>
        /// <returns>用于方法链的客户端构建器。</returns>
        [Experimental("StreamingJsonSerializationExperimental", UrlFormat = "https://github.com/dotnet/orleans/pull/9618")]
        public static IClientBuilder AddAzureQueueJsonStreams(this IClientBuilder builder,
            string name, Action<OptionsBuilder<AzureQueueOptions>> configureOptions)
        {
            builder.AddAzureQueueJsonStreams(name, b => b.ConfigureAzureQueue(configureOptions));
            return builder;
        }
    }
}
