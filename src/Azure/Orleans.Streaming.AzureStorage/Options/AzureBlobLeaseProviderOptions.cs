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
    /// 用于配置以 Azure Blob Storage 租约为后端的租约提供程序的选项。
    /// </summary>
    public class AzureBlobLeaseProviderOptions
    {
        private BlobServiceClient? _blobServiceClient;

        /// <summary>
        /// 获取或设置存储租约 Blob 的 Azure Blob 容器名称。
        /// </summary>
        public string BlobContainerName { get; set; } = DefaultBlobContainerName;

        /// <summary>
        /// 存储租约 Blob 的默认 Azure Blob 容器名称。
        /// </summary>
        public const string DefaultBlobContainerName = "Leases";

        /// <summary>
        /// 配置 Blob Storage 客户端时使用的选项，或使用 <see langword="null"/> 采用默认选项。
        /// </summary>
        [Obsolete($"Set the {nameof(BlobServiceClient)} property directly.")]
        public BlobClientOptions? ClientOptions { get; set; }

        /// <summary>
        /// 用于创建 <see cref="BlobServiceClient"/> 实例的可选委托。
        /// </summary>
        internal Func<Task<BlobServiceClient>>? CreateClient { get; private set; }

        /// <summary>
        /// 获取或设置用于访问 Azure Blob 服务的客户端。
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
        /// 使用连接字符串配置 <see cref="BlobServiceClient"/>。
        /// </summary>
        [Obsolete($"Set the {nameof(BlobServiceClient)} property directly.")]
        public void ConfigureBlobServiceClient(string connectionString)
        {
            BlobServiceClient = new BlobServiceClient(connectionString, ClientOptions);
        }

        /// <summary>
        /// 使用经过身份验证的服务 URI 配置 <see cref="BlobServiceClient"/>。
        /// </summary>
        [Obsolete($"Set the {nameof(BlobServiceClient)} property directly.")]
        public void ConfigureBlobServiceClient(Uri serviceUri)
        {
            BlobServiceClient = new BlobServiceClient(serviceUri, ClientOptions);
        }

        /// <summary>
        /// 使用提供的回调配置 <see cref="BlobServiceClient"/>。
        /// </summary>
        [Obsolete($"Set the {nameof(BlobServiceClient)} property directly.")]
        public void ConfigureBlobServiceClient(Func<Task<BlobServiceClient>> createClientCallback)
        {
            CreateClient = createClientCallback ?? throw new ArgumentNullException(nameof(createClientCallback));
        }

        /// <summary>
        /// 配置 <see cref="BlobServiceClient"/>，使用经过身份验证的服务 URI 和 <see cref="Azure.Core.TokenCredential"/>。
        /// </summary>
        [Obsolete($"Set the {nameof(BlobServiceClient)} property directly.")]
        public void ConfigureBlobServiceClient(Uri serviceUri, TokenCredential tokenCredential)
        {
            BlobServiceClient = new BlobServiceClient(serviceUri, tokenCredential, ClientOptions);
        }

        /// <summary>
        /// 配置 <see cref="BlobServiceClient"/>，使用经过身份验证的服务 URI 和 <see cref="Azure.AzureSasCredential"/>。
        /// </summary>
        [Obsolete($"Set the {nameof(BlobServiceClient)} property directly.")]
        public void ConfigureBlobServiceClient(Uri serviceUri, AzureSasCredential azureSasCredential)
        {
            BlobServiceClient = new BlobServiceClient(serviceUri, azureSasCredential, ClientOptions);
        }

        /// <summary>
        /// 配置 <see cref="BlobServiceClient"/>，使用经过身份验证的服务 URI 和 <see cref="StorageSharedKeyCredential"/>。
        /// </summary>
        [Obsolete($"Set the {nameof(BlobServiceClient)} property directly.")]
        public void ConfigureBlobServiceClient(Uri serviceUri, StorageSharedKeyCredential sharedKeyCredential)
        {
            BlobServiceClient = new BlobServiceClient(serviceUri, sharedKeyCredential, ClientOptions);
        }
    }

    /// <summary>
    /// AzureBlobLeaseProviderOptions 的配置验证器。
    /// </summary>
    public class AzureBlobLeaseProviderOptionsValidator : IConfigurationValidator
    {
        private readonly AzureBlobLeaseProviderOptions options;
        private readonly string? name;

        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="options">要验证的选项。</param>
        [SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "Microsoft.Extensions.DependencyInjection supplies the registered options instance.")]
        public AzureBlobLeaseProviderOptionsValidator(IOptions<AzureBlobLeaseProviderOptions> options)
        {
            this.options = options.Value;
        }

        /// <summary>
        /// 为命名的 AzureBlobLeaseProviderOptions 选项实例创建验证器。
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
        /// 构造函数。
        /// </summary>
        /// <param name="options">要验证的选项。</param>
        /// <param name="name">要验证的选项名称。</param>
        private AzureBlobLeaseProviderOptionsValidator(AzureBlobLeaseProviderOptions options, string? name)
        {
            this.options = options;
            this.name = name ?? string.Empty;
        }

        /// <summary>
        /// 验证配置的容器名称和 Azure Blob 服务客户端。
        /// </summary>
        /// <exception cref="OrleansConfigurationException">配置无效。</exception>
        public void ValidateConfiguration()
        {
            // 名称可以为 null，但不能是空字符串或仅包含空白字符。
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
