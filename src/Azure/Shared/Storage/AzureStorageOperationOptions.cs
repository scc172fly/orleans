using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Orleans.Runtime;

#if ORLEANS_CLUSTERING
namespace Orleans.Clustering.AzureStorage
#elif ORLEANS_PERSISTENCE
namespace Orleans.Persistence.AzureStorage
#elif ORLEANS_REMINDERS
namespace Orleans.Reminders.AzureStorage
#elif ORLEANS_STREAMING
namespace Orleans.Streaming.AzureStorage
#elif ORLEANS_EVENTHUBS
namespace Orleans.Streaming.EventHubs
#elif TESTER_AZUREUTILS
namespace Orleans.Tests.AzureUtils
#elif ORLEANS_TRANSACTIONS
namespace Orleans.Transactions.AzureStorage
#elif ORLEANS_DIRECTORY
namespace Orleans.GrainDirectory.AzureStorage
#else
// 有意不使用默认命名空间，以便未定义某项内容时产生编译错误。
#endif
{
    /// <summary>
    /// 配置对 Azure Table Storage 表的访问。
    /// </summary>
    public class AzureStorageOperationOptions
    {
        private TableServiceClient? _tableServiceClient;

        /// <summary>
        /// Azure Storage 表名。
        /// </summary>
        public virtual string TableName { get; set; } = null!;

        /// <summary>
        /// Azure Storage 策略选项。
        /// </summary>
        public AzureStoragePolicyOptions StoragePolicyOptions { get; } = new AzureStoragePolicyOptions();

        /// <summary>
        /// 配置 Table Storage 客户端时使用的选项，或使用 <see langword="null"/> 采用默认选项。
        /// </summary>
        public TableClientOptions? ClientOptions { get; set; }

        /// <summary>
        /// 用于创建 <see cref="TableServiceClient"/> 实例的委托。
        /// </summary>
        internal Func<Task<TableServiceClient>> CreateClient { get; private set; } = null!;

        /// <summary>
        /// 获取或设置用于访问 Azure Table 服务的客户端。
        /// </summary>
        [DisallowNull]
        public TableServiceClient? TableServiceClient
        {
            get => _tableServiceClient;
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                _tableServiceClient = value;
                CreateClient = () => Task.FromResult(value);
            }
        }

        /// <summary>
        /// 使用连接字符串配置 <see cref="TableServiceClient"/>。
        /// </summary>
        [Obsolete($"Set the {nameof(TableServiceClient)} property directly.")]
        public void ConfigureTableServiceClient(string connectionString)
        {
            SetTableServiceClient(connectionString);
        }

        /// <summary>
        /// 使用经过身份验证的服务 URI 配置 <see cref="TableServiceClient"/>。
        /// </summary>
        [Obsolete($"Set the {nameof(TableServiceClient)} property directly.")]
        public void ConfigureTableServiceClient(Uri serviceUri)
        {
            SetTableServiceClient(serviceUri);
        }

        /// <summary>
        /// 使用提供的回调配置 <see cref="TableServiceClient"/>。
        /// </summary>
        [Obsolete($"Set the {nameof(TableServiceClient)} property directly.")]
        public void ConfigureTableServiceClient(Func<Task<TableServiceClient>> createClientCallback)
        {
            CreateClient = createClientCallback ?? throw new ArgumentNullException(nameof(createClientCallback));
        }

        /// <summary>
        /// 配置 <see cref="TableServiceClient"/>，使用经过身份验证的服务 URI 和 <see cref="Azure.Core.TokenCredential"/>。
        /// </summary>
        [Obsolete($"Set the {nameof(TableServiceClient)} property directly.")]
        public void ConfigureTableServiceClient(Uri serviceUri, TokenCredential tokenCredential)
        {
            SetTableServiceClient(serviceUri, tokenCredential);
        }

        /// <summary>
        /// 配置 <see cref="TableServiceClient"/>，使用经过身份验证的服务 URI 和 <see cref="Azure.AzureSasCredential"/>。
        /// </summary>
        [Obsolete($"Set the {nameof(TableServiceClient)} property directly.")]
        public void ConfigureTableServiceClient(Uri serviceUri, AzureSasCredential azureSasCredential)
        {
            SetTableServiceClient(serviceUri, azureSasCredential);
        }

        /// <summary>
        /// 配置 <see cref="TableServiceClient"/>，使用经过身份验证的服务 URI 和 <see cref="TableSharedKeyCredential"/>。
        /// </summary>
        [Obsolete($"Set the {nameof(TableServiceClient)} property directly.")]
        public void ConfigureTableServiceClient(Uri serviceUri, TableSharedKeyCredential sharedKeyCredential)
        {
            SetTableServiceClient(serviceUri, sharedKeyCredential);
        }

        internal void SetTableServiceClient(string connectionString)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentNullException(nameof(connectionString));
            _tableServiceClient = null;
            CreateClient = () => Task.FromResult(new TableServiceClient(connectionString, GetTableClientOptions()));
        }

        internal void SetTableServiceClient(Uri serviceUri)
        {
            if (serviceUri is null) throw new ArgumentNullException(nameof(serviceUri));
            _tableServiceClient = null;
            CreateClient = () => Task.FromResult(new TableServiceClient(serviceUri, GetTableClientOptions()));
        }

        internal void SetTableServiceClient(Uri serviceUri, TokenCredential tokenCredential)
        {
            if (serviceUri is null) throw new ArgumentNullException(nameof(serviceUri));
            if (tokenCredential is null) throw new ArgumentNullException(nameof(tokenCredential));
            _tableServiceClient = null;
            CreateClient = () => Task.FromResult(new TableServiceClient(serviceUri, tokenCredential, GetTableClientOptions()));
        }

        internal void SetTableServiceClient(Uri serviceUri, AzureSasCredential azureSasCredential)
        {
            if (serviceUri is null) throw new ArgumentNullException(nameof(serviceUri));
            if (azureSasCredential is null) throw new ArgumentNullException(nameof(azureSasCredential));
            _tableServiceClient = null;
            CreateClient = () => Task.FromResult(new TableServiceClient(serviceUri, azureSasCredential, GetTableClientOptions()));
        }

        internal void SetTableServiceClient(Uri serviceUri, TableSharedKeyCredential sharedKeyCredential)
        {
            if (serviceUri is null) throw new ArgumentNullException(nameof(serviceUri));
            if (sharedKeyCredential is null) throw new ArgumentNullException(nameof(sharedKeyCredential));
            _tableServiceClient = null;
            CreateClient = () => Task.FromResult(new TableServiceClient(serviceUri, sharedKeyCredential, GetTableClientOptions()));
        }

        internal TableClientOptions GetTableClientOptions()
        {
            var clientOptions = ClientOptions ??= new TableClientOptions();
            ConfigureRetryOptions(clientOptions.Retry, StoragePolicyOptions);
            return clientOptions;
        }

        private static void ConfigureRetryOptions(RetryOptions retryOptions, AzureStoragePolicyOptions storagePolicyOptions)
        {
            // 保持此处与 AzureStoragePolicyOptions 的默认值一致，后者与 Azure Storage SDK 的重试设置一致。
            var delay = storagePolicyOptions.PauseBetweenOperationRetries > TimeSpan.Zero
                ? storagePolicyOptions.PauseBetweenOperationRetries
                : TimeSpan.FromSeconds(0.8);

            var maxDelay = storagePolicyOptions.MaxPauseBetweenOperationRetries == Timeout.InfiniteTimeSpan
                ? TimeSpan.FromMinutes(1)
                : storagePolicyOptions.MaxPauseBetweenOperationRetries;

            retryOptions.Mode = RetryMode.Exponential;
            retryOptions.Delay = delay;
            retryOptions.MaxDelay = maxDelay >= delay ? maxDelay : delay;
            retryOptions.MaxRetries = Math.Max(0, storagePolicyOptions.MaxOperationRetries);
        }

        internal void Validate(string? name)
        {
            if (CreateClient is null)
            {
                throw new OrleansConfigurationException($"No credentials specified. Use the {GetType().Name}.{nameof(ConfigureTableServiceClient)} method to configure the Azure Table Service client.");
            }

            try
            {
                AzureTableUtils.ValidateTableName(TableName);
            }
            catch (Exception ex)
            {
                throw GetException($"{nameof(TableName)} is not valid.", ex);
            }

            Exception GetException(string message, Exception? inner = null) =>
                new OrleansConfigurationException($"Configuration for {GetType().Name} {name} is invalid. {message}", inner!);
        }
    }

    /// <summary>
    /// 验证 Azure Table Storage 操作选项。
    /// </summary>
    /// <typeparam name="TOptions">要验证的选项类型。</typeparam>
    public class AzureStorageOperationOptionsValidator<TOptions> : IConfigurationValidator where TOptions : AzureStorageOperationOptions
    {
        /// <summary>
        /// 初始化 <see cref="AzureStorageOperationOptionsValidator{TOptions}"/> 类的新实例。
        /// </summary>
        /// <param name="options">要验证的选项。</param>
        /// <param name="name">已配置的选项名称。</param>
        public AzureStorageOperationOptionsValidator(TOptions options, string? name = null)
        {
            Options = options;
            Name = name;
        }

        /// <summary>
        /// 获取要验证的选项。
        /// </summary>
        public TOptions Options { get; }

        /// <summary>
        /// 获取已配置的选项名称。
        /// </summary>
        public string? Name { get; }

        /// <inheritdoc />
        public virtual void ValidateConfiguration()
        {
            Options.Validate(Name);
        }
    }
}
