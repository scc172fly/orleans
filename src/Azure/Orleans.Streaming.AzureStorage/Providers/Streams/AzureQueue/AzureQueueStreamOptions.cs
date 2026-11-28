using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Azure.Storage;
using Azure.Storage.Queues;
using Orleans.AzureUtils;
using Orleans.Runtime;

namespace Orleans.Configuration
{
    /// <summary>
    /// Azure Queue 流提供程序选项。
    /// </summary>
    public class AzureQueueOptions
    {
        private QueueServiceClient? _queueServiceClient;

        /// <summary>
        /// 配置 Queue Storage 客户端时使用的选项，或使用 <see langword="null"/> 采用默认选项。
        /// </summary>
        [Obsolete($"Set the {nameof(QueueServiceClient)} property directly.")]
        public QueueClientOptions ClientOptions { get; set; } = new QueueClientOptions
        {
            Retry =
            {
                Mode = RetryMode.Fixed,
                Delay = AzureQueueDefaultPolicies.PauseBetweenQueueOperationRetries,
                MaxRetries = AzureQueueDefaultPolicies.MaxQueueOperationRetries,
                NetworkTimeout = AzureQueueDefaultPolicies.QueueOperationTimeout,
            }
        };

        /// <summary>
        /// 用于创建 <see cref="QueueServiceClient"/> 实例的可选委托。
        /// </summary>
        internal Func<Task<QueueServiceClient>>? CreateClient { get; private set; }

        /// <summary>
        /// 获取或设置已接收的消息对其他队列消费者保持不可见的时间间隔。
        /// 值为 <see langword="null"/> 时使用 Azure Queue 服务的默认值。
        /// </summary>
        public TimeSpan? MessageVisibilityTimeout { get; set; }

        /// <summary>
        /// 获取或设置用于划分流提供程序分区的 Azure 队列名称。
        /// </summary>
        /// <remarks>
        /// 如果未配置此列表，标准 Silo 和客户端配置器将根据服务名称及提供程序名称生成队列名称。
        /// </remarks>
        public List<string> QueueNames { get; set; } = null!;

        /// <summary>
        /// 获取或设置用于访问 Azure Queue 服务的 <see cref="QueueServiceClient"/>。
        /// </summary>
        [DisallowNull]
        public QueueServiceClient? QueueServiceClient
        {
            get => _queueServiceClient;

            set
            {
                ArgumentNullException.ThrowIfNull(value);
                _queueServiceClient = value;
                CreateClient = () => Task.FromResult(value);
            }
        }

        /// <summary>
        /// 使用连接字符串配置 <see cref="QueueServiceClient"/>。
        /// </summary>
        [Obsolete($"Set the {nameof(QueueServiceClient)} property directly.")]
        public void ConfigureQueueServiceClient(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentNullException(nameof(connectionString));
            CreateClient = () => Task.FromResult(new QueueServiceClient(connectionString, ClientOptions));
        }

        /// <summary>
        /// 使用经过身份验证的服务 URI 配置 <see cref="QueueServiceClient"/>。
        /// </summary>
        [Obsolete($"Set the {nameof(QueueServiceClient)} property directly.")]
        public void ConfigureQueueServiceClient(Uri serviceUri)
        {
            if (serviceUri is null) throw new ArgumentNullException(nameof(serviceUri));
            CreateClient = () => Task.FromResult(new QueueServiceClient(serviceUri, ClientOptions));
        }

        /// <summary>
        /// 使用提供的回调配置 <see cref="QueueServiceClient"/>。
        /// </summary>
        [Obsolete($"Set the {nameof(QueueServiceClient)} property directly.")]
        public void ConfigureQueueServiceClient(Func<Task<QueueServiceClient>> createClientCallback)
        {
            CreateClient = createClientCallback ?? throw new ArgumentNullException(nameof(createClientCallback));
        }

        /// <summary>
        /// 配置 <see cref="QueueServiceClient"/>，使用经过身份验证的服务 URI 和 <see cref="Azure.Core.TokenCredential"/>。
        /// </summary>
        [Obsolete($"Set the {nameof(QueueServiceClient)} property directly.")]
        public void ConfigureQueueServiceClient(Uri serviceUri, TokenCredential tokenCredential)
        {
            if (serviceUri is null) throw new ArgumentNullException(nameof(serviceUri));
            if (tokenCredential is null) throw new ArgumentNullException(nameof(tokenCredential));
            CreateClient = () => Task.FromResult(new QueueServiceClient(serviceUri, tokenCredential, ClientOptions));
        }

        /// <summary>
        /// 配置 <see cref="QueueServiceClient"/>，使用经过身份验证的服务 URI 和 <see cref="Azure.AzureSasCredential"/>。
        /// </summary>
        [Obsolete($"Set the {nameof(QueueServiceClient)} property directly.")]
        public void ConfigureQueueServiceClient(Uri serviceUri, AzureSasCredential azureSasCredential)
        {
            if (serviceUri is null) throw new ArgumentNullException(nameof(serviceUri));
            if (azureSasCredential is null) throw new ArgumentNullException(nameof(azureSasCredential));
            CreateClient = () => Task.FromResult(new QueueServiceClient(serviceUri, azureSasCredential, ClientOptions));
        }

        /// <summary>
        /// 配置 <see cref="QueueServiceClient"/>，使用经过身份验证的服务 URI 和 <see cref="StorageSharedKeyCredential"/>。
        /// </summary>
        [Obsolete($"Set the {nameof(QueueServiceClient)} property directly.")]
        public void ConfigureQueueServiceClient(Uri serviceUri, StorageSharedKeyCredential sharedKeyCredential)
        {
            if (serviceUri is null) throw new ArgumentNullException(nameof(serviceUri));
            if (sharedKeyCredential is null) throw new ArgumentNullException(nameof(sharedKeyCredential));
            CreateClient = () => Task.FromResult(new QueueServiceClient(serviceUri, sharedKeyCredential, ClientOptions));
        }
    }

    /// <summary>
    /// 验证命名的 <see cref="AzureQueueOptions"/> 实例。
    /// </summary>
    public class AzureQueueOptionsValidator : IConfigurationValidator
    {
        private readonly AzureQueueOptions options;
        private readonly string name;

        private AzureQueueOptionsValidator(AzureQueueOptions options, string name)
        {
            this.options = options;
            this.name = name;
        }

        /// <summary>
        /// 验证 Azure Queue 服务客户端及队列名称是否已配置。
        /// </summary>
        /// <exception cref="OrleansConfigurationException">配置无效。</exception>
        public void ValidateConfiguration()
        {
            if (this.options.CreateClient is null)
            {
                throw new OrleansConfigurationException($"No credentials specified. Use the {options.GetType().Name}.{nameof(AzureQueueOptions.ConfigureQueueServiceClient)} method to configure the Azure Queue Service client.");
            }

            if (options.QueueNames == null || options.QueueNames.Count == 0)
                throw new OrleansConfigurationException(
                    $"{nameof(AzureQueueOptions)} on stream provider {this.name} is invalid. {nameof(AzureQueueOptions.QueueNames)} is invalid");
        }

        /// <summary>
        /// 为命名的 Azure Queue 流提供程序选项创建验证器。
        /// </summary>
        /// <param name="services">服务提供程序。</param>
        /// <param name="name">流提供程序名称。</param>
        /// <returns>配置验证器。</returns>
        public static IConfigurationValidator Create(IServiceProvider services, string name)
        {
            AzureQueueOptions aqOptions = services.GetOptionsByName<AzureQueueOptions>(name);
            return new AzureQueueOptionsValidator(aqOptions, name);
        }
    }
}
