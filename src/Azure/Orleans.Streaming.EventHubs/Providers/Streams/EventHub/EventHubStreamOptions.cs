using Azure;
using Azure.Core;
using Azure.Messaging.EventHubs;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Runtime;
using Orleans.Streams;
using System;

namespace Orleans.Configuration
{
    /// <summary>
    /// 特定中心EventHub 设置
    /// </summary>
    public class EventHubOptions
    {
        /// <summary>
        /// 获取代表用来创建与 Azure 事件枢纽的连接。
        /// </summary>
        internal CreateConnectionDelegate CreateConnection { get; private set; } = null!;

        /// <summary>
        /// 活动枢纽消费者团体。
        /// </summary>
        internal string ConsumerGroup { get; private set; } = null!;

        /// <summary>
        /// 事件中心名称 。
        /// </summary>
        internal string EventHubName { get; private set; } = null!;

        /// <summary>
        /// 创建 Azure 事件枢纽连接时使用的连接选项。
        /// </summary>
        public EventHubConnectionOptions ConnectionOptions { get; set; } = new EventHubConnectionOptions { TransportType = EventHubsTransportType.AmqpTcp };

        /// <summary>
        /// 创建 Azure 事件枢纽连接。
        /// </summary>
        /// <param name="connectionOptions">连接选项。</param>
        /// <returns>A 音 A Azure 活动枢纽连接。</returns>
        public delegate EventHubConnection CreateConnectionDelegate(EventHubConnectionOptions connectionOptions);

        /// <summary>
        /// 配置 Azure 事件枢纽连接,使用提供的连接字符串。
        /// </summary>
        /// <param name="connectionString">事件枢纽连接字符串。</param>
        /// <param name="eventHubName">活动中心名称。</param>
        /// <param name="consumerGroup">消费者团体名称。</param>
        public void ConfigureEventHubConnection(string connectionString, string eventHubName, string consumerGroup)
        {
            EventHubName = eventHubName;
            ConsumerGroup = consumerGroup;

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new ArgumentException("A non-null, non-empty value must be provided.", nameof(connectionString));
            }

            ValidateValues(eventHubName, consumerGroup);

            CreateConnection = connectionOptions => new EventHubConnection(connectionString, EventHubName, connectionOptions);
        }

        /// <summary>
        /// 配置 Azure 事件枢纽连接,使用所提供的完全合格的命名空间字符串和证书。
        /// </summary>
        /// <param name="fullyQualifiedNamespace">完全合格的事件枢纽命名空间。</param>
        /// <param name="eventHubName">活动中心名称。</param>
        /// <param name="consumerGroup">消费者团体名称。</param>
        /// <param name="credential">名号密钥证书。</param>
        public void ConfigureEventHubConnection(string fullyQualifiedNamespace, string eventHubName, string consumerGroup, AzureNamedKeyCredential credential)
        {
            EventHubName = eventHubName;
            ConsumerGroup = consumerGroup;

            if (string.IsNullOrWhiteSpace(fullyQualifiedNamespace))
            {
                throw new ArgumentException("A non-null, non-empty value must be provided.", nameof(fullyQualifiedNamespace));
            }

            ValidateValues(eventHubName, consumerGroup);

            if (credential is null)
            {
                throw new ArgumentNullException(nameof(credential));
            }

            CreateConnection = connectionOptions => new EventHubConnection(fullyQualifiedNamespace, EventHubName, credential, connectionOptions);
        }

        /// <summary>
        /// 配置 Azure 事件枢纽连接,使用所提供的完全合格的命名空间字符串和证书。
        /// </summary>
        /// <param name="fullyQualifiedNamespace">完全合格的事件枢纽命名空间。</param>
        /// <param name="eventHubName">活动中心名称。</param>
        /// <param name="consumerGroup">消费者团体名称。</param>
        /// <param name="credential">共享存取签名证书。</param>
        public void ConfigureEventHubConnection(string fullyQualifiedNamespace, string eventHubName, string consumerGroup, AzureSasCredential credential)
        {
            EventHubName = eventHubName;
            ConsumerGroup = consumerGroup;

            if (string.IsNullOrWhiteSpace(fullyQualifiedNamespace))
            {
                throw new ArgumentException("A non-null, non-empty value must be provided.", nameof(fullyQualifiedNamespace));
            }

            ValidateValues(eventHubName, consumerGroup);

            if (credential is null)
            {
                throw new ArgumentNullException(nameof(credential));
            }

            CreateConnection = connectionOptions => new EventHubConnection(fullyQualifiedNamespace, EventHubName, credential, connectionOptions);
        }

        /// <summary>
        /// 配置 Azure 事件枢纽连接,使用所提供的完全合格的命名空间字符串和证书。
        /// </summary>
        /// <param name="fullyQualifiedNamespace">完全合格的事件枢纽命名空间。</param>
        /// <param name="eventHubName">活动中心名称。</param>
        /// <param name="consumerGroup">消费者团体名称。</param>
        /// <param name="credential">象征性证书。</param>
        public void ConfigureEventHubConnection(string fullyQualifiedNamespace, string eventHubName, string consumerGroup, TokenCredential credential)
        {
            EventHubName = eventHubName;
            ConsumerGroup = consumerGroup;
            if (string.IsNullOrWhiteSpace(fullyQualifiedNamespace))
            {
                throw new ArgumentException("A non-null, non-empty value must be provided.", nameof(fullyQualifiedNamespace));
            }

            ValidateValues(eventHubName, consumerGroup);
            if (credential is null)
            {
                throw new ArgumentNullException(nameof(credential));
            }

            CreateConnection = connectionOptions => new EventHubConnection(fullyQualifiedNamespace, EventHubName, credential, connectionOptions);
        }

        /// <summary>
        /// 配置使用提供的连接实例的 Azure 事件枢纽连接。
        /// </summary>
        /// <param name="connection">事件枢纽连接。</param>
        /// <param name="consumerGroup">消费者团体名称。</param>
        public void ConfigureEventHubConnection(EventHubConnection connection, string consumerGroup)
        {
            EventHubName = connection.EventHubName;
            ConsumerGroup = consumerGroup;
            ValidateValues(connection.EventHubName, consumerGroup);
            if (connection is null) throw new ArgumentNullException(nameof(connection));
            CreateConnection = _ => connection;
        }

        /// <summary>
        /// 配置使用所提供代表的 Azure 事件枢纽连接。
        /// </summary>
        /// <param name="createConnection">该代表用来创建事件枢纽连接。</param>
        /// <param name="eventHubName">活动中心名称。</param>
        /// <param name="consumerGroup">消费者团体名称。</param>
        public void ConfigureEventHubConnection(CreateConnectionDelegate createConnection, string eventHubName, string consumerGroup)
        {
            EventHubName = eventHubName;
            ConsumerGroup = consumerGroup;
            ValidateValues(eventHubName, consumerGroup);
            CreateConnection = createConnection ?? throw new ArgumentNullException(nameof(createConnection));
        }

        private static void ValidateValues(string eventHubName, string consumerGroup)
        {
            if (string.IsNullOrWhiteSpace(eventHubName))
            {
                throw new ArgumentException("A non-null, non-empty value must be provided.", nameof(eventHubName));
            }

            if (string.IsNullOrWhiteSpace(consumerGroup))
            {
                throw new ArgumentException("A non-null, non-empty value must be provided.", nameof(consumerGroup));
            }
        }
    }

    /// <summary>
    /// 验证 <see cref="EventHubOptions"/>。
    /// </summary>
    public class EventHubOptionsValidator : IConfigurationValidator
    {
        private readonly EventHubOptions options;
        private readonly string name;
        /// <summary>
        /// 初始化 <see cref="EventHubOptionsValidator"/> 类的新实例。
        /// </summary>
        /// <param name="options">验证的选项。</param>
        /// <param name="name">流体提供者名称。</param>
        public EventHubOptionsValidator(EventHubOptions options, string name)
        {
            this.options = options;
            this.name = name;
        }
        /// <inheritdoc />
        public void ValidateConfiguration()
        {
            if (options.CreateConnection is null)
            {
                throw new OrleansConfigurationException($"Azure Event Hub connection not configured for stream provider options {nameof(EventHubOptions)} with name \"{name}\". Use the {options.GetType().Name}.{nameof(EventHubOptions.ConfigureEventHubConnection)} method to configure the connection.");
            }

            if (string.IsNullOrEmpty(options.ConsumerGroup))
            {
                throw new OrleansConfigurationException($"{nameof(EventHubOptions)} on stream provider {this.name} is invalid. {nameof(EventHubOptions.ConsumerGroup)} is invalid");
            }

            if (string.IsNullOrEmpty(options.EventHubName))
            {
                throw new OrleansConfigurationException($"{nameof(EventHubOptions)} on stream provider {this.name} is invalid. {nameof(EventHubOptions.EventHubName)} is invalid");
            }
        }
    }

    /// <summary>
    /// 校验为事件枢纽流提供者配置的串列列检查器 。
    /// </summary>
    public class StreamCheckpointerConfigurationValidator : IConfigurationValidator
    {
        private readonly IServiceProvider services;
        private readonly string name;
        /// <summary>
        /// 初始化 <see cref="StreamCheckpointerConfigurationValidator"/> 类的新实例。
        /// </summary>
        /// <param name="services">服务供应商。</param>
        /// <param name="name">流体提供者名称。</param>
        public StreamCheckpointerConfigurationValidator(IServiceProvider services, string name)
        {
            this.services = services;
            this.name = name;
        }
        /// <inheritdoc />
        public void ValidateConfiguration()
        {
            var checkpointerFactory = services.GetKeyedService<IStreamQueueCheckpointerFactory>(this.name);
            if (checkpointerFactory == null)
                throw new OrleansConfigurationException($"No IStreamQueueCheckpointer is configured with PersistentStreamProvider {this.name}. Please configure one.");
        }
    }

    /// <summary>
    /// 配置事件枢纽流提供者如何接收每个分区的事件 。
    /// </summary>
    public class EventHubReceiverOptions
    {
        /// <summary>
        /// 配置接收器预取数的可选参数。
        /// </summary>
        public int? PrefetchCount { get; set; }
        /// <summary>
        /// 如果没有设立检查站,则表示服务是从最新数据或从分隔区开始算起。
        /// </summary>
        public bool StartFromNow { get; set; } = DEFAULT_START_FROM_NOW;
        private const bool DEFAULT_START_FROM_NOW = true;
    }

    /// <summary>
    /// 配置事件枢纽流提供者的缓存压力监测 。
    /// </summary>
    public class EventHubStreamCachePressureOptions
    {
        /// <summary>
        /// SlowConsumingPressureMonitorConfig
        /// </summary>
        public double? SlowConsumingMonitorFlowControlThreshold { get; set; }

        /// <summary>
        /// SlowConsumingMonitorPressureWindowSize
        /// </summary>
        public TimeSpan? SlowConsumingMonitorPressureWindowSize { get; set; }

        /// <summary>
        /// AveragingCachePressureMonitorFlowControlThreshold, AveragingCachePressureMonitor 默认打开。 
        /// 用户可以通过将此值设为空来关闭它
        /// </summary>
        public double? AveragingCachePressureMonitorFlowControlThreshold { get; set; } = DEFAULT_AVERAGING_CACHE_PRESSURE_MONITORING_THRESHOLD;
        internal const double DEFAULT_AVERAGING_CACHE_PRESSURE_MONITORING_THRESHOLD = 1.0 / 3.0;
    }
}
