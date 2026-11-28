using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Streaming.Kinesis;

namespace Orleans.Hosting
{
    /// <summary>
    /// 在 Orleans 客户端上配置由 Amazon Kinesis Data Streams 支持的持久化流提供程序。
    /// </summary>
    public class ClusterClientKinesisStreamConfigurator : ClusterClientPersistentStreamConfigurator
    {
        /// <summary>
        /// 初始化 <see cref="ClusterClientKinesisStreamConfigurator"/> 类的新实例。
        /// </summary>
        /// <param name="name">流提供程序名称。</param>
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
        /// 为流提供程序配置 Kinesis 选项。
        /// </summary>
        /// <param name="configureOptions">
        /// 用于配置具名 <see cref="KinesisStreamOptions"/> 的委托。
        /// </param>
        /// <returns>流提供程序配置器。</returns>
        public ClusterClientKinesisStreamConfigurator ConfigureKinesis(Action<OptionsBuilder<KinesisStreamOptions>> configureOptions)
        {
            this.Configure(configureOptions);
            return this;
        }

        /// <summary>
        /// 为流提供程序配置 Kinesis 选项。
        /// </summary>
        /// <param name="configureOptions">用于配置 Kinesis 选项的委托。</param>
        /// <returns>流提供程序配置器。</returns>
        public ClusterClientKinesisStreamConfigurator ConfigureKinesis(Action<KinesisStreamOptions> configureOptions)
        {
            this.ConfigureKinesis(ob => ob.Configure(configureOptions));
            return this;
        }
    }
}
