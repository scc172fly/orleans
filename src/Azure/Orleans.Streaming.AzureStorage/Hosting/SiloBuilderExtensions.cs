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
    /// 用于在 <see cref="ISiloBuilder"/> 上配置 Azure Queue 流和 Azure Blob 租约提供程序的扩展方法。
    /// </summary>
    public static class SiloBuilderExtensions
    {
        /// <summary>
        /// 配置 Silo 使用 Azure Queue 持久化流。
        /// </summary>
        public static ISiloBuilder AddAzureQueueStreams(this ISiloBuilder builder, string name,
            Action<SiloAzureQueueStreamConfigurator> configure)
        {
            var configurator = new SiloAzureQueueStreamConfigurator(name, configureServicesDelegate => builder.ConfigureServices(configureServicesDelegate));
            configure?.Invoke(configurator);
            return builder;
        }

        /// <summary>
        /// 配置 Silo 使用采用默认设置的 Azure Queue 持久化流。
        /// </summary>
        public static ISiloBuilder AddAzureQueueStreams(this ISiloBuilder builder, string name, Action<OptionsBuilder<AzureQueueOptions>> configureOptions)
        {
            builder.AddAzureQueueStreams(name, b => b.ConfigureAzureQueue(configureOptions));
            return builder;
        }

        /// <summary>
        /// 配置 Silo 使用采用 JSON 序列化的 Azure Queue 持久化流。
        /// 此功能处于实验阶段，未来更新中可能发生变化。
        /// </summary>
        /// <param name="builder">Silo 构建器。</param>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="configure">启用 JSON 的 Azure Queue 流提供程序的配置委托。</param>
        /// <returns>用于链式调用的 Silo 构建器。</returns>
        [Experimental("StreamingJsonSerializationExperimental", UrlFormat = "https://github.com/dotnet/orleans/pull/9618")]
        public static ISiloBuilder AddAzureQueueJsonStreams(this ISiloBuilder builder, string name,
            Action<SiloAzureQueueJsonStreamConfigurator> configure)
        {
            var configurator = new SiloAzureQueueJsonStreamConfigurator(name, configureServicesDelegate => builder.ConfigureServices(configureServicesDelegate));
            configure?.Invoke(configurator);
            return builder;
        }

        /// <summary>
        /// 配置 Silo 使用采用 JSON 序列化和默认设置的 Azure Queue 持久化流。
        /// 此功能处于实验阶段，未来更新中可能发生变化。
        /// </summary>
        /// <param name="builder">Silo 构建器。</param>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="configureOptions">Azure Queue 选项的配置委托。</param>
        /// <returns>用于链式调用的 Silo 构建器。</returns>
        [Experimental("StreamingJsonSerializationExperimental", UrlFormat = "https://github.com/dotnet/orleans/pull/9618")]
        public static ISiloBuilder AddAzureQueueJsonStreams(this ISiloBuilder builder, string name, Action<OptionsBuilder<AzureQueueOptions>> configureOptions)
        {
            builder.AddAzureQueueJsonStreams(name, b => b.ConfigureAzureQueue(configureOptions));
            return builder;
        }

        /// <summary>
        /// 配置 Silo 使用 Azure Blob 租约提供程序。
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
        /// 配置 Silo 使用 Azure Blob 租约提供程序。
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="configurator"/> 或 <paramref name="configureOptions"/> 为 <see langword="null"/>。</exception>
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
