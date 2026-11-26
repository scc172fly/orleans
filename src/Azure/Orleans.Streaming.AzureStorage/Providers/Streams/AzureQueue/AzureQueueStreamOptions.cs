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
    /// Azure 队列流提供者选项。
    /// </summary>
    public class AzureQueueOptions
    {
        private QueueServiceClient? _queueServiceClient;

        /// <summary>
        /// 配置队列存储客户端时, 或使用默认选项时使用的选项, 或者 <see langword="null"/> 。
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
        /// 用于创建 <see cref="QueueServiceClient"/> 实例的可选代表 。
        /// </summary>
        internal Func<Task<QueueServiceClient>>? CreateClient { get; private set; }

        /// <summary>
        /// 获取或设置接收到的信件仍然为其他队列用户所隐蔽的间隔 。
        /// <see langword="null"/> 值使用 Azure 队列服务默认值。
        /// </summary>
        public TimeSpan? MessageVisibilityTimeout { get; set; }

        /// <summary>
        /// 获取或设置用于分隔流提供方的 Azure 队列的名称。
        /// </summary>
        /// <remarks>
        /// 标准 silo 和客户端配置器在不配置此列表时从服务名和提供者名中生成队列名称 。
        /// </remarks>
        public List<string> QueueNames { get; set; } = null!;

        /// <summary>
        /// 设置 <see cref="QueueServiceClient"/> 访问用于 Azure 服务队
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
        /// 使用连接字符串配置 <see cref="QueueServiceClient"/> 。
        /// </summary>
        [Obsolete($"Set the {nameof(QueueServiceClient)} property directly.")]
        public void ConfigureQueueServiceClient(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentNullException(nameof(connectionString));
            CreateClient = () => Task.FromResult(new QueueServiceClient(connectionString, ClientOptions));
        }

        /// <summary>
        /// 使用验证的服务 URI 配置 <see cref="QueueServiceClient"/> 。
        /// </summary>
        [Obsolete($"Set the {nameof(QueueServiceClient)} property directly.")]
        public void ConfigureQueueServiceClient(Uri serviceUri)
        {
            if (serviceUri is null) throw new ArgumentNullException(nameof(serviceUri));
            CreateClient = () => Task.FromResult(new QueueServiceClient(serviceUri, ClientOptions));
        }

        /// <summary>
        /// 使用所提供的回调配置 <see cref="QueueServiceClient"/> 。
        /// </summary>
        [Obsolete($"Set the {nameof(QueueServiceClient)} property directly.")]
        public void ConfigureQueueServiceClient(Func<Task<QueueServiceClient>> createClientCallback)
        {
            CreateClient = createClientCallback ?? throw new ArgumentNullException(nameof(createClientCallback));
        }

        /// <summary>
        /// 配置 <see cref="QueueServiceClient"/> 使用认证服务 URI 和 a <see cref="Azure.Core.TokenCredential"/>.
        /// </summary>
        [Obsolete($"Set the {nameof(QueueServiceClient)} property directly.")]
        public void ConfigureQueueServiceClient(Uri serviceUri, TokenCredential tokenCredential)
        {
            if (serviceUri is null) throw new ArgumentNullException(nameof(serviceUri));
            if (tokenCredential is null) throw new ArgumentNullException(nameof(tokenCredential));
            CreateClient = () => Task.FromResult(new QueueServiceClient(serviceUri, tokenCredential, ClientOptions));
        }

        /// <summary>
        /// 配置 <see cref="QueueServiceClient"/> 使用认证服务 URI 和 a <see cref="Azure.AzureSasCredential"/>.
        /// </summary>
        [Obsolete($"Set the {nameof(QueueServiceClient)} property directly.")]
        public void ConfigureQueueServiceClient(Uri serviceUri, AzureSasCredential azureSasCredential)
        {
            if (serviceUri is null) throw new ArgumentNullException(nameof(serviceUri));
            if (azureSasCredential is null) throw new ArgumentNullException(nameof(azureSasCredential));
            CreateClient = () => Task.FromResult(new QueueServiceClient(serviceUri, azureSasCredential, ClientOptions));
        }

        /// <summary>
        /// 配置 <see cref="QueueServiceClient"/> 使用认证服务 URI 和 a <see cref="StorageSharedKeyCredential"/>.
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
    /// 被命名为 <see cref="AzureQueueOptions"/> 实例的校验。
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
        /// 验证 Azure 队列服务客户端和队列名称是否配置。
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
        /// 为命名的 Azure 队列流提供者选项创建验证符。
        /// </summary>
        /// <param name="services">服务供应商。</param>
        /// <param name="name">流体提供者名称。</param>
        /// <returns>配置验证符。</returns>
        public static IConfigurationValidator Create(IServiceProvider services, string name)
        {
            AzureQueueOptions aqOptions = services.GetOptionsByName<AzureQueueOptions>(name);
            return new AzureQueueOptionsValidator(aqOptions, name);
        }
    }
}
