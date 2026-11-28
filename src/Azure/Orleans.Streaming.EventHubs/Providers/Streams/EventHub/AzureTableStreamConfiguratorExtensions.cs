using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Runtime;
using Orleans.Streams;

namespace Orleans.Hosting
{
    /// <summary>
    /// 用于配置 Azure Table 流检查点管理器的扩展方法。
    /// </summary>
    public static class AzureTableStreamConfiguratorExtensions
    {
        /// <summary>
        /// 配置流提供程序使用 Azure Table Storage 持久化检查点。
        /// </summary>
        /// <param name="configurator">配置构建器。</param>
        /// <param name="configureOptions">Azure Table 检查点管理器配置。</param>
        public static void UseAzureTableCheckpointer(
            this ISiloPersistentStreamConfigurator configurator,
            Action<OptionsBuilder<AzureTableStreamCheckpointerOptions>> configureOptions)
        {
            configurator.ConfigureDelegate(services =>
                services.AddTransient<IConfigurationValidator>(sp =>
                    new AzureTableStreamCheckpointerOptionsValidator(
                        sp.GetRequiredService<IOptionsMonitor<AzureTableStreamCheckpointerOptions>>().Get(configurator.Name),
                        configurator.Name)));
            configurator.ConfigureComponent<AzureTableStreamCheckpointerOptions, IStreamQueueCheckpointerFactory>(
                AzureTableStreamQueueCheckpointerFactory.CreateFactory,
                options =>
                {
                    options.Validate(
                        static value => value.PersistInterval > TimeSpan.Zero,
                        $"{nameof(AzureTableStreamCheckpointerOptions.PersistInterval)} must be greater than zero.");
                    configureOptions(options);
                });
        }
    }
}
