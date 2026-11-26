using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Azure.Storage;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;
using Orleans.Runtime;
using Orleans.Streaming.AzureStorage;

namespace Orleans.Configuration
{
    /// <summary>
    /// 配置一个以 Azure 布洛布存储租赁为后盾的租赁供应商的备选方案。
    /// </summary>
    public class AzureBlobLeaseProviderOptions
    {
        private BlobServiceClient? _blobServiceClient;

        /// <summary>
        /// 获取或设置用于存储租赁布束的 Azure 布束容器的名称。
        /// </summary>
        public string BlobContainerName { get; set; } = DefaultBlobContainerName;

        /// <summary>
        /// 用于存储租赁布料的默认 Azure 布洛布容器名称 。
        /// </summary>
        public const string DefaultBlobContainerName = "Leases";

        /// <summary>
        /// 当配置 blob 存储客户端时, 或者 <see langword="null"/> 使用默认选项时要使用的选项 。
        /// </summary>
        [Obsolete($"Set the {nameof(BlobServiceClient)} property directly.")]
        public BlobClientOptions? ClientOptions { get; set; }

        /// <summary>
        /// 用于创建 <see cref="BlobServiceClient"/> 实例的可选代表 。
        /// </summary>
        internal Func<Task<BlobServiceClient>>? CreateClient { get; private set; }

        /// <summary>
        /// 获取或设置用于访问 Azure 球形服务的客户端。
        /// </summary>
        [DisallowNull]
        public BlobServiceClient? BlobServiceClient
        {
            get => _blobServiceClient;
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                _blobServiceClient = value;
                CreateClient = () => Task.FromResult(value);
            }
        }

        /// <summary>
        /// 使用连接字符串配置 <see cref="BlobServiceClient"/> 。
        /// </summary>
        [Obsolete($"Set the {nameof(BlobServiceClient)} property directly.")]
        public void ConfigureBlobServiceClient(string connectionString)
        {
            BlobServiceClient = new BlobServiceClient(connectionString, ClientOptions);
        }

        /// <summary>
        /// 使用验证的服务 URI 配置 <see cref="BlobServiceClient"/> 。
        /// </summary>
        [Obsolete($"Set the {nameof(BlobServiceClient)} property directly.")]
        public void ConfigureBlobServiceClient(Uri serviceUri)
        {
            BlobServiceClient = new BlobServiceClient(serviceUri, ClientOptions);
        }

        /// <summary>
        /// 使用所提供的回调配置 <see cref="BlobServiceClient"/> 。
        /// </summary>
        [Obsolete($"Set the {nameof(BlobServiceClient)} property directly.")]
        public void ConfigureBlobServiceClient(Func<Task<BlobServiceClient>> createClientCallback)
        {
            CreateClient = createClientCallback ?? throw new ArgumentNullException(nameof(createClientCallback));
        }

        /// <summary>
        /// 配置 <see cref="BlobServiceClient"/> 使用认证服务 URI 和 a <see cref="Azure.Core.TokenCredential"/>.
        /// </summary>
        [Obsolete($"Set the {nameof(BlobServiceClient)} property directly.")]
        public void ConfigureBlobServiceClient(Uri serviceUri, TokenCredential tokenCredential)
        {
            BlobServiceClient = new BlobServiceClient(serviceUri, tokenCredential, ClientOptions);
        }

        /// <summary>
        /// 配置 <see cref="BlobServiceClient"/> 使用认证服务 URI 和 a <see cref="Azure.AzureSasCredential"/>.
        /// </summary>
        [Obsolete($"Set the {nameof(BlobServiceClient)} property directly.")]
        public void ConfigureBlobServiceClient(Uri serviceUri, AzureSasCredential azureSasCredential)
        {
            BlobServiceClient = new BlobServiceClient(serviceUri, azureSasCredential, ClientOptions);
        }

        /// <summary>
        /// 配置 <see cref="BlobServiceClient"/> 使用认证服务 URI 和 a <see cref="StorageSharedKeyCredential"/>.
        /// </summary>
        [Obsolete($"Set the {nameof(BlobServiceClient)} property directly.")]
        public void ConfigureBlobServiceClient(Uri serviceUri, StorageSharedKeyCredential sharedKeyCredential)
        {
            BlobServiceClient = new BlobServiceClient(serviceUri, sharedKeyCredential, ClientOptions);
        }
    }

    /// <summary>
    /// AzureBlobLeaseProviderOptions的配置验证符
    /// </summary>
    public class AzureBlobLeaseProviderOptionsValidator : IConfigurationValidator
    {
        private readonly AzureBlobLeaseProviderOptions options;
        private readonly string? name;

        /// <summary>
        /// 构造建构器
        /// </summary>
        /// <param name="options">选项将生效。</param>
        [SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "Microsoft.Extensions.DependencyInjection supplies the registered options instance.")]
        public AzureBlobLeaseProviderOptionsValidator(IOptions<AzureBlobLeaseProviderOptions> options)
        {
            this.options = options.Value;
        }

        /// <summary>
        /// 创建指定 AzureBlobLeaseProviderOptions 选项实例的验证符。
        /// </summary>
        /// <param name="services"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        public static IConfigurationValidator Create(IServiceProvider services, string name)
        {
            var options = services.GetOptionsByName<AzureBlobLeaseProviderOptions>(name);
            return new AzureBlobLeaseProviderOptionsValidator(options, name);
        }

        /// <summary>
        /// 构造建构器
        /// </summary>
        /// <param name="options">选项将生效。</param>
        /// <param name="name">需要验证的选项名称。</param>
        private AzureBlobLeaseProviderOptionsValidator(AzureBlobLeaseProviderOptions options, string? name)
        {
            this.options = options;
            this.name = name ?? string.Empty;
        }

        /// <summary>
        /// 校验配置的容器名称和 Azure Blob Service 客户端。
        /// </summary>
        /// <exception cref="OrleansConfigurationException">配置无效。</exception>
        public void ValidateConfiguration()
        {
            // 名称可以是空的,但不是空白或白的空格。
            if (this.name != null && string.IsNullOrWhiteSpace(this.name))
            {
                throw new OrleansConfigurationException($"Named option {nameof(AzureBlobLeaseProviderOptions)} of name {this.name} is invalid.  Name cannot be empty or whitespace.");
            }

            if (this.options.CreateClient is null)
            {
                throw new OrleansConfigurationException($"No credentials specified for Azure Blob Service lease provider \"{name}\". Use the {options.GetType().Name}.{nameof(AzureBlobLeaseProviderOptions.ConfigureBlobServiceClient)} method to configure the Azure Blob Service client.");
            }

            try
            {
                AzureBlobUtils.ValidateContainerName(this.options.BlobContainerName);
            }
            catch (ArgumentException e)
            {
                var errorStr = string.IsNullOrEmpty(this.name)
                    ? $"Configuration for {nameof(AzureBlobLeaseProviderOptions)} {this.name} is invalid. {nameof(this.options.BlobContainerName)} is not valid"
                    : $"Configuration for {nameof(AzureBlobLeaseProviderOptions)} is invalid. {nameof(this.options.BlobContainerName)} is not valid";
                throw new OrleansConfigurationException(errorStr, e);
            }
        }
    }

}
