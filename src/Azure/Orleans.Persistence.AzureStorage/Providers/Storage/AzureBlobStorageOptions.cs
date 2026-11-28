using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Azure;
using Azure.Core;
using Azure.Storage;
using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Persistence.AzureStorage;
using Orleans.Runtime;
using Orleans.Storage;

namespace Orleans.Configuration
{
    /// <summary>
    /// 配置 Azure Blob Grain 存储提供程序。
    /// </summary>
    public class AzureBlobStorageOptions : IStorageProviderSerializerOptions
    {
        private BlobServiceClient? _blobServiceClient;

        /// <summary>
        /// 存储 Grain 状态的容器名称。
        /// </summary>
        public string ContainerName { get; set; } = DEFAULT_CONTAINER_NAME;

        /// <summary>
        /// 用于存储 Grain 状态的默认 Azure Blob 容器名称。
        /// </summary>
        public const string DEFAULT_CONTAINER_NAME = "grainstate";

        /// <summary>
        /// 配置 Blob Storage 客户端时使用的选项，或使用 <see langword="null"/> 采用默认选项。
        /// </summary>
        public BlobClientOptions? ClientOptions { get; set; }

        /// <summary>
        /// 用于创建 <see cref="BlobServiceClient"/> 实例的可选委托。
        /// </summary>
        internal Func<Task<BlobServiceClient>> CreateClient { get; private set; } = null!;

        /// <summary>
        /// 应初始化存储的 Silo 生命周期阶段。存储必须在使用前完成初始化。
        /// </summary>
        public int InitStage { get; set; } = DEFAULT_INIT_STAGE;

        /// <summary>
        /// 初始化存储提供程序的默认 Silo 生命周期阶段。
        /// </summary>
        public const int DEFAULT_INIT_STAGE = ServiceLifecycleStage.ApplicationServices;

        /// <inheritdoc/>
        public IGrainStorageSerializer GrainStorageSerializer { get; set; } = null!;

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
        /// 获取或设置一个值，指示调用 <see cref="IGrainStorage.ClearStateAsync{T}(string, GrainId, IGrainState{T})"/> 时是否删除状态。默认为 true。
        /// </summary>
        public bool DeleteStateOnClear { get; set; } = true;

        /// <summary>
        /// 用于构建容器工厂实例的函数。
        /// </summary>
        public Func<IServiceProvider, AzureBlobStorageOptions, IBlobContainerFactory> BuildContainerFactory { get; set; }
            = static (provider, options) => ActivatorUtilities.CreateInstance<DefaultBlobContainerFactory>(provider, options);

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
    /// AzureBlobStorageOptions 的配置验证器。
    /// </summary>
    public class AzureBlobStorageOptionsValidator : IConfigurationValidator
    {
        private readonly AzureBlobStorageOptions options;
        private readonly string name;

        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="options">要验证的选项。</param>
        /// <param name="name">要验证的选项名称。</param>
        public AzureBlobStorageOptionsValidator(AzureBlobStorageOptions options, string name)
        {
            this.options = options;
            this.name = name;
        }

        /// <inheritdoc/>
        public void ValidateConfiguration()
        {
            if (this.options.CreateClient is null)
            {
                throw new OrleansConfigurationException($"No credentials specified. Use the {options.GetType().Name}.{nameof(AzureBlobStorageOptions.ConfigureBlobServiceClient)} method to configure the Azure Blob Service client.");
            }

            try
            {
                AzureBlobUtils.ValidateContainerName(options.ContainerName);
                AzureBlobUtils.ValidateBlobName(this.name);
            }
            catch (ArgumentException e)
            {
                throw new OrleansConfigurationException(
                    $"Configuration for AzureBlobStorageOptions {name} is invalid. {nameof(this.options.ContainerName)} is not valid", e);
            }
        }
    }
}
