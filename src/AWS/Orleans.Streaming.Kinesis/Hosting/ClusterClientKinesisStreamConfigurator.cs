using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Streaming.Kinesis;

namespace Orleans.Hosting
{
    /// <summary>
    /// 配置一个 Amazon Kinesis 数据流支持的 持续流提供商, 在一个 Orleans 客户端上 。
    /// </summary>
    public class ClusterClientKinesisStreamConfigurator : ClusterClientPersistentStreamConfigurator
    {
        /// <summary>
        /// 初始化 <see cref="ClusterClientKinesisStreamConfigurator"/> 类的新实例。
        /// </summary>
        /// <param name="name">流体提供者的名称。</param>
        /// <param name="builder">客户端构建器。</param>
        public ClusterClientKinesisStreamConfigurator(string name, IClientBuilder builder)
            : base(name, builder, KinesisAdapterFactory.Create)
        {
            this.ConfigureDelegate(services =>
            {
                services.ConfigureNamedOptionForLogging<KinesisStreamOptions>(name)
                    .ConfigureNamedOptionForLogging<HashRingStreamQueueMapperOptions>(name)
                    .AddTransient<IConfigurationValidator>(sp => new KinesisStreamOptionsValidator(sp.GetOptionsByName<KinesisStreamOptions>(name), name));
            });
        }

        /// <summary>
        /// 配置流提供方的 Kinesis 选项 。
        /// </summary>
        /// <param name="configureOptions">
        /// 用于配置命名 <see cref="KinesisStreamOptions"/>的代表。
        /// </param>
        /// <returns>流体提供者配置器。</returns>
        public ClusterClientKinesisStreamConfigurator ConfigureKinesis(Action<OptionsBuilder<KinesisStreamOptions>> configureOptions)
        {
            this.Configure(configureOptions);
            return this;
        }

        /// <summary>
        /// 配置流提供方的 Kinesis 选项 。
        /// </summary>
        /// <param name="configureOptions">代表用于配置 Kinesis 选项。</param>
        /// <returns>流体提供者配置器。</returns>
        public ClusterClientKinesisStreamConfigurator ConfigureKinesis(Action<KinesisStreamOptions> configureOptions)
        {
            this.ConfigureKinesis(ob => ob.Configure(configureOptions));
            return this;
        }
    }
}
