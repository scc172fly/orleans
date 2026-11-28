using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Streaming.Kinesis;
using Orleans.Streams;

namespace Orleans.Hosting
{
    /// <summary>
    /// 在 Orleans Silo 上配置由 Amazon Kinesis Data Streams 支持的持久化流提供程序。
    /// </summary>
    public class SiloKinesisStreamConfigurator : SiloPersistentStreamConfigurator
    {
        /// <summary>
        /// 初始化 <see cref="SiloKinesisStreamConfigurator"/> 类的新实例。
        /// </summary>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="configureServicesDelegate">用于配置 Silo 服务的委托。</param>
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
        /// 为流提供程序配置 Kinesis 选项。
        /// </summary>
        /// <param name="configureOptions">
        /// 用于配置具名 <see cref="KinesisStreamOptions"/> 的委托。
        /// </param>
        /// <returns>流提供程序配置器。</returns>
        public SiloKinesisStreamConfigurator ConfigureKinesis(Action<OptionsBuilder<KinesisStreamOptions>> configureOptions)
        {
            this.Configure(configureOptions);
            return this;
        }

        /// <summary>
        /// 为流提供程序配置 Kinesis 选项。
        /// </summary>
        /// <param name="configureOptions">用于配置 Kinesis 选项的委托。</param>
        /// <returns>流提供程序配置器。</returns>
        public SiloKinesisStreamConfigurator ConfigureKinesis(Action<KinesisStreamOptions> configureOptions)
        {
            this.ConfigureKinesis(ob => ob.Configure(configureOptions));
            return this;
        }

        /// <summary>
        /// 配置用于持久化各分片流检查点的组件。
        /// </summary>
        /// <typeparam name="TOptions">检查点记录器使用的选项类型。</typeparam>
        /// <param name="checkpointerFactoryBuilder">
        /// 使用服务提供程序和流提供程序名称调用的工厂，用于创建检查点记录器工厂。
        /// </param>
        /// <param name="configureOptions">用于配置具名检查点记录器选项的委托。</param>
        /// <returns>流提供程序配置器。</returns>
        public SiloKinesisStreamConfigurator ConfigureCheckpointer<TOptions>(
            Func<IServiceProvider, string, IStreamQueueCheckpointerFactory> checkpointerFactoryBuilder,
             Action<OptionsBuilder<TOptions>> configureOptions)
            where TOptions : class, new()
        {
            this.ConfigureComponent(checkpointerFactoryBuilder, configureOptions);
            return this;
        }

        /// <summary>
        /// 配置流提供程序将检查点持久化到 DynamoDB。
        /// </summary>
        /// <param name="configureOptions">用于配置 DynamoDB 检查点记录器选项的委托。</param>
        /// <returns>流提供程序配置器。</returns>
        public SiloKinesisStreamConfigurator UseDynamoDBCheckpointer(
            Action<DynamoDBStreamQueueCheckpointerOptions> configureOptions)
            => UseDynamoDBCheckpointer(options => options.Configure(configureOptions));

        /// <summary>
        /// 配置流提供程序将检查点持久化到 DynamoDB。
        /// </summary>
        /// <param name="configureOptions">
        /// 用于配置具名 DynamoDB 检查点记录器选项的可选委托。
        /// </param>
        /// <returns>流提供程序配置器。</returns>
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
