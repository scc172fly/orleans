using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Configuration.Internal;
using Orleans.LeaseProviders;

namespace Orleans.Hosting
{
    /// <summary>
    /// 配置 Azure 队列流和 Azure 布罗布租赁供应商在 <see cref="ISiloBuilder"/>上的扩展方法。
    /// </summary>
    public static class SiloBuilderExtensions
    {
        /// <summary>
        /// 配置 silo 使用“ 紫色” 队列持久性流。
        /// </summary>
        public static ISiloBuilder AddAzureQueueStreams(this ISiloBuilder builder, string name,
            Action<SiloAzureQueueStreamConfigurator> configure)
        {
            var configurator = new SiloAzureQueueStreamConfigurator(name, configureServicesDelegate => builder.ConfigureServices(configureServicesDelegate));
            configure?.Invoke(configurator);
            return builder;
        }

        /// <summary>
        /// 配置 silo , 使用默认设置的“ zure” 队列持久性流
        /// </summary>
        public static ISiloBuilder AddAzureQueueStreams(this ISiloBuilder builder, string name, Action<OptionsBuilder<AzureQueueOptions>> configureOptions)
        {
            builder.AddAzureQueueStreams(name, b => b.ConfigureAzureQueue(configureOptions));
            return builder;
        }

        /// <summary>
        /// 配置 silo 使用 Azure 将 JSON 序列化的持久性流排成队列。
        /// 这是试验性的,今后更新时会改变。
        /// </summary>
        /// <param name="builder">下 silo 建筑商</param>
        /// <param name="name">流体提供者名称。</param>
        /// <param name="configure"> JSON- 启用的 Azure 队列流提供者配置代表。</param>
        /// <returns>下 silo 方法链的建筑工。</returns>
        [Experimental("StreamingJsonSerializationExperimental", UrlFormat = "https://github.com/dotnet/orleans/pull/9618")]
        public static ISiloBuilder AddAzureQueueJsonStreams(this ISiloBuilder builder, string name,
            Action<SiloAzureQueueJsonStreamConfigurator> configure)
        {
            var configurator = new SiloAzureQueueJsonStreamConfigurator(name, configureServicesDelegate => builder.ConfigureServices(configureServicesDelegate));
            configure?.Invoke(configurator);
            return builder;
        }

        /// <summary>
        /// 配置 silo 使用 Azure 将具有 JSON 序列化和默认设置的持久性流排队。
        /// 这是试验性的,今后更新时会改变。
        /// </summary>
        /// <param name="builder">下 silo 建筑商</param>
        /// <param name="name">流体提供者名称。</param>
        /// <param name="configureOptions">配置代表 Azure 队列选项。</param>
        /// <returns>下 silo 方法链的建筑工。</returns>
        [Experimental("StreamingJsonSerializationExperimental", UrlFormat = "https://github.com/dotnet/orleans/pull/9618")]
        public static ISiloBuilder AddAzureQueueJsonStreams(this ISiloBuilder builder, string name, Action<OptionsBuilder<AzureQueueOptions>> configureOptions)
        {
            builder.AddAzureQueueJsonStreams(name, b => b.ConfigureAzureQueue(configureOptions));
            return builder;
        }

        /// <summary>
        /// 配置 silo 使用 azure blob 租赁供应商
        /// </summary>
        public static ISiloBuilder UseAzureBlobLeaseProvider(this ISiloBuilder builder, Action<OptionsBuilder<AzureBlobLeaseProviderOptions>> configureOptions)
        {
            builder.ConfigureServices(services => ConfigureAzureBlobLeaseProviderServices(services, configureOptions));
            return builder;
        }

        private static void ConfigureAzureBlobLeaseProviderServices(IServiceCollection services, Action<OptionsBuilder<AzureBlobLeaseProviderOptions>> configureOptions)
        {
            configureOptions?.Invoke(services.AddOptions<AzureBlobLeaseProviderOptions>());
            services.AddTransient<IConfigurationValidator, AzureBlobLeaseProviderOptionsValidator>();
            services.ConfigureFormatter<AzureBlobLeaseProviderOptions>();
            services.AddTransient<AzureBlobLeaseProvider>();
            services.AddFromExisting<ILeaseProvider, AzureBlobLeaseProvider>();
        }

        /// <summary>
        /// 配置 silo 使用 azure blob 租赁供应商
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="configurator"/> 或 <paramref name="configureOptions"/> 是 <see langword="null"/>。</exception>
        public static void UseAzureBlobLeaseProvider(this ISiloPersistentStreamConfigurator configurator, Action<OptionsBuilder<AzureBlobLeaseProviderOptions>> configureOptions)
        {
            ArgumentNullException.ThrowIfNull(configurator);
            ArgumentNullException.ThrowIfNull(configureOptions);

            configurator.ConfigureDelegate(services =>
            {
                services.AddTransient(sp => AzureBlobLeaseProviderOptionsValidator.Create(sp, configurator.Name));
            });
            configurator.ConfigureComponent(AzureBlobLeaseProvider.Create, configureOptions);
        }
    }
}
