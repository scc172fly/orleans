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
    /// 特定 Event Hub 的设置。
    /// </summary>
    public class EventHubOptions
    {
        /// <summary>
        /// 获取用于创建 Azure Event Hub 连接的委托。
        /// </summary>
        internal CreateConnectionDelegate CreateConnection { get; private set; } = null!;

        /// <summary>
        /// Event Hub 消费者组。
        /// </summary>
        internal string ConsumerGroup { get; private set; } = null!;

        /// <summary>
        /// Event Hub 名称。
        /// </summary>
        internal string EventHubName { get; private set; } = null!;

        /// <summary>
        /// 创建 Azure Event Hub 连接时使用的连接选项。
        /// </summary>
        public EventHubConnectionOptions ConnectionOptions { get; set; } = new EventHubConnectionOptions { TransportType = EventHubsTransportType.AmqpTcp };

        /// <summary>
        /// 创建 Azure Event Hub 连接。
        /// </summary>
        /// <param name="connectionOptions">连接选项。</param>
        /// <returns>Azure Event Hub 连接。</returns>
        public delegate EventHubConnection CreateConnectionDelegate(EventHubConnectionOptions connectionOptions);

        /// <summary>
        /// 使用提供的连接字符串配置 Azure Event Hub 连接。
        /// </summary>
        /// <param name="connectionString">Event Hub 连接字符串。</param>
        /// <param name="eventHubName">Event Hub 名称。</param>
        /// <param name="consumerGroup">消费者组名称。</param>
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
        /// 使用提供的完全限定命名空间字符串及凭据配置 Azure Event Hub 连接。
        /// </summary>
        /// <param name="fullyQualifiedNamespace">完全限定的 Event Hubs 命名空间。</param>
        /// <param name="eventHubName">Event Hub 名称。</param>
        /// <param name="consumerGroup">消费者组名称。</param>
        /// <param name="credential">命名密钥凭据。</param>
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
        /// 使用提供的完全限定命名空间字符串及凭据配置 Azure Event Hub 连接。
        /// </summary>
        /// <param name="fullyQualifiedNamespace">完全限定的 Event Hubs 命名空间。</param>
        /// <param name="eventHubName">Event Hub 名称。</param>
        /// <param name="consumerGroup">消费者组名称。</param>
        /// <param name="credential">共享访问签名凭据。</param>
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
        /// 使用提供的完全限定命名空间字符串及凭据配置 Azure Event Hub 连接。
        /// </summary>
        /// <param name="fullyQualifiedNamespace">完全限定的 Event Hubs 命名空间。</param>
        /// <param name="eventHubName">Event Hub 名称。</param>
        /// <param name="consumerGroup">消费者组名称。</param>
        /// <param name="credential">令牌凭据。</param>
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
        /// 使用提供的连接实例配置 Azure Event Hub 连接。
        /// </summary>
        /// <param name="connection">Event Hub 连接。</param>
        /// <param name="consumerGroup">消费者组名称。</param>
        public void ConfigureEventHubConnection(EventHubConnection connection, string consumerGroup)
        {
            EventHubName = connection.EventHubName;
            ConsumerGroup = consumerGroup;
            ValidateValues(connection.EventHubName, consumerGroup);
            if (connection is null) throw new ArgumentNullException(nameof(connection));
            CreateConnection = _ => connection;
        }

        /// <summary>
        /// 使用提供的委托配置 Azure Event Hub 连接。
        /// </summary>
        /// <param name="createConnection">用于创建 Event Hub 连接的委托。</param>
        /// <param name="eventHubName">Event Hub 名称。</param>
        /// <param name="consumerGroup">消费者组名称。</param>
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
        /// <param name="options">要验证的选项。</param>
        /// <param name="name">流提供程序名称。</param>
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
    /// 验证 Event Hubs 流提供程序是否已配置流队列检查点管理器。
    /// </summary>
    public class StreamCheckpointerConfigurationValidator : IConfigurationValidator
    {
        private readonly IServiceProvider services;
        private readonly string name;
        /// <summary>
        /// 初始化 <see cref="StreamCheckpointerConfigurationValidator"/> 类的新实例。
        /// </summary>
        /// <param name="services">服务提供程序。</param>
        /// <param name="name">流提供程序名称。</param>
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
    /// 配置 Event Hubs 流提供程序从每个分区接收事件的方式。
    /// </summary>
    public class EventHubReceiverOptions
    {
        /// <summary>
        /// 用于配置接收器预取数量的可选参数。
        /// </summary>
        public int? PrefetchCount { get; set; }
        /// <summary>
        /// 如果未找到检查点，此值指示服务应从最新数据开始读取，还是从分区开头开始读取。
        /// </summary>
        public bool StartFromNow { get; set; } = DEFAULT_START_FROM_NOW;
        private const bool DEFAULT_START_FROM_NOW = true;
    }

    /// <summary>
    /// 为 Event Hubs 流提供程序配置缓存压力监控。
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
        /// AveragingCachePressureMonitorFlowControlThreshold，默认启用 AveragingCachePressureMonitor。
        /// 用户可将此值设置为 null 来禁用它。
        /// </summary>
        public double? AveragingCachePressureMonitorFlowControlThreshold { get; set; } = DEFAULT_AVERAGING_CACHE_PRESSURE_MONITORING_THRESHOLD;
        internal const double DEFAULT_AVERAGING_CACHE_PRESSURE_MONITORING_THRESHOLD = 1.0 / 3.0;
    }
}
