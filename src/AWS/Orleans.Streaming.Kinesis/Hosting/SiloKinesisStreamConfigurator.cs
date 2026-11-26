using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Streaming.Kinesis;
using Orleans.Streams;

namespace Orleans.Hosting
{
    /// <summary>
    /// 配置 Orleans silo上的亚马逊 Kinesis数据流支持的持久性流提供者。
    /// </summary>
    public class SiloKinesisStreamConfigurator : SiloPersistentStreamConfigurator
    {
        /// <summary>
        /// 初始化 <see cref="SiloKinesisStreamConfigurator"/> 类的新实例。
        /// </summary>
        /// <param name="name">流体提供者的名称。</param>
        /// <param name="configureServicesDelegate">代表用于配置 silo 服务。</param>
        public SiloKinesisStreamConfigurator(string name, Action<Action<IServiceCollection>> configureServicesDelegate)
            : base(name, configureServicesDelegate, KinesisAdapterFactory.Create)
        {
            this.ConfigureDelegate(services =>
            {
                services.ConfigureNamedOptionForLogging<KinesisStreamOptions>(name)
                    .ConfigureNamedOptionForLogging<SimpleQueueCacheOptions>(name)
                    .ConfigureNamedOptionForLogging<HashRingStreamQueueMapperOptions>(name)
                    .AddTransient<IConfigurationValidator>(sp => new KinesisStreamOptionsValidator(sp.GetOptionsByName<KinesisStreamOptions>(name), name))
                .AddTransient<IConfigurationValidator>(sp => new KinesisStreamCheckpointerConfigurationValidator(sp, name));
            });
        }

        /// <summary>
        /// 配置流提供方的 Kinesis 选项 。
        /// </summary>
        /// <param name="configureOptions">
        /// 用于配置命名 <see cref="KinesisStreamOptions"/>的代表。
        /// </param>
        /// <returns>流体提供者配置器。</returns>
        public SiloKinesisStreamConfigurator ConfigureKinesis(Action<OptionsBuilder<KinesisStreamOptions>> configureOptions)
        {
            this.Configure(configureOptions);
            return this;
        }

        /// <summary>
        /// 配置流提供方的 Kinesis 选项 。
        /// </summary>
        /// <param name="configureOptions">代表用于配置 Kinesis 选项。</param>
        /// <returns>流体提供者配置器。</returns>
        public SiloKinesisStreamConfigurator ConfigureKinesis(Action<KinesisStreamOptions> configureOptions)
        {
            this.ConfigureKinesis(ob => ob.Configure(configureOptions));
            return this;
        }

        /// <summary>
        /// 配置用于维持每个硬流检查站的组件。
        /// </summary>
        /// <typeparam name="TOptions">检查站使用的选项类型。</typeparam>
        /// <param name="checkpointerFactoryBuilder">
        /// 工厂与服务提供商和溪流供应商名称一起援引创建检查站厂。
        /// </param>
        /// <param name="configureOptions">代表用来配置命名的检查站选项。</param>
        /// <returns>流体提供者配置器。</returns>
        public SiloKinesisStreamConfigurator ConfigureCheckpointer<TOptions>(
            Func<IServiceProvider, string, IStreamQueueCheckpointerFactory> checkpointerFactoryBuilder,
             Action<OptionsBuilder<TOptions>> configureOptions)
            where TOptions : class, new()
        {
            this.ConfigureComponent(checkpointerFactoryBuilder, configureOptions);
            return this;
        }

        /// <summary>
        /// 配置流提供方到 DynamoDB的持久性检查站。
        /// </summary>
        /// <param name="configureOptions">代表用于配置 DynamoDB 检查站选项。</param>
        /// <returns>流体提供者配置器。</returns>
        public SiloKinesisStreamConfigurator UseDynamoDBCheckpointer(
            Action<DynamoDBStreamQueueCheckpointerOptions> configureOptions)
            => UseDynamoDBCheckpointer(options => options.Configure(configureOptions));

        /// <summary>
        /// 配置流提供方到 DynamoDB的持久性检查站。
        /// </summary>
        /// <param name="configureOptions">
        /// 用于配置命名 DynamoDB 检查器选项的可选代表。
        /// </param>
        /// <returns>流体提供者配置器。</returns>
        public SiloKinesisStreamConfigurator UseDynamoDBCheckpointer(
            Action<OptionsBuilder<DynamoDBStreamQueueCheckpointerOptions>>? configureOptions = null)
        {
            ConfigureCheckpointer<DynamoDBStreamQueueCheckpointerOptions>(
                DynamoDBStreamQueueCheckpointerFactory.CreateFactory,
                options => configureOptions?.Invoke(options));
            this.ConfigureDelegate(services => services.AddTransient<IConfigurationValidator>(
                sp => new DynamoDBStreamQueueCheckpointerOptionsValidator(
                    sp.GetOptionsByName<DynamoDBStreamQueueCheckpointerOptions>(Name),
                    Name)));
            return this;
        }
    }

    internal sealed class KinesisStreamCheckpointerConfigurationValidator(
        IServiceProvider services,
        string name) : IConfigurationValidator
    {
        public void ValidateConfiguration()
        {
            if (services.GetKeyedService<IStreamQueueCheckpointerFactory>(name) is null)
            {
                throw new OrleansConfigurationException(
                    $"No IStreamQueueCheckpointer is configured with PersistentStreamProvider {name}. Please configure one.");
            }
        }
    }
}
