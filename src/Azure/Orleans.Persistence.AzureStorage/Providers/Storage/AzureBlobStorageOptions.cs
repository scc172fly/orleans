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
    /// 配置 Azure 布行 grain 存储提供者。
    /// </summary>
    public class AzureBlobStorageOptions : IStorageProviderSerializerOptions
    {
        private BlobServiceClient? _blobServiceClient;

        /// <summary>
        /// 储存 grain 阶段的容器名称
        /// </summary>
        public string ContainerName { get; set; } = DEFAULT_CONTAINER_NAME;

        /// <summary>
        /// 默认默认 Azure 存储时使用的布布容器名称 grain 状态。
        /// </summary>
        public const string DEFAULT_CONTAINER_NAME = "grainstate";

        /// <summary>
        /// 当配置 blob 存储客户端时, 或者 <see langword="null"/> 使用默认选项时要使用的选项 。
        /// </summary>
        public BlobClientOptions? ClientOptions { get; set; }

        /// <summary>
        /// 用于创建 <see cref="BlobServiceClient"/> 实例的可选代表 。
        /// </summary>
        internal Func<Task<BlobServiceClient>> CreateClient { get; private set; } = null!;

        /// <summary>
        /// 储存应初始化的 silo 生命周期阶段,储存必须在使用前初始化。
        /// </summary>
        public int InitStage { get; set; } = DEFAULT_INIT_STAGE;

        /// <summary>
        /// 存储提供者初始化的默认 silo 生命周期阶段。
        /// </summary>
        public const int DEFAULT_INIT_STAGE = ServiceLifecycleStage.ApplicationServices;

        /// <inheritdoc/>
        public IGrainStorageSerializer GrainStorageSerializer { get; set; } = null!;

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
        /// 获取或设置值,以显示是否在调用 <see cref="IGrainStorage.ClearStateAsync{T}(string, GrainId, IGrainState{T})"/> 时删除状态。默认为真。
        /// </summary>
        public bool DeleteStateOnClear { get; set; } = true;

        /// <summary>
        /// 建造集装箱工厂的功能
        /// </summary>
        public Func<IServiceProvider, AzureBlobStorageOptions, IBlobContainerFactory> BuildContainerFactory { get; set; }
            = static (provider, options) => ActivatorUtilities.CreateInstance<DefaultBlobContainerFactory>(provider, options);

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
    /// AzureBlobStorageOptions的配置验证符
    /// </summary>
    public class AzureBlobStorageOptionsValidator : IConfigurationValidator
    {
        private readonly AzureBlobStorageOptions options;
        private readonly string name;

        /// <summary>
        /// 构造建构器
        /// </summary>
        /// <param name="options">选项将生效。</param>
        /// <param name="name">需要验证的选项名称。</param>
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
