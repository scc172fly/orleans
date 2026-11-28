using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Orleans.Configuration;
using Orleans.Runtime;

namespace Orleans.Storage;

/// <summary>
/// 使用 grainType 和 grainId 构建 Blob Storage 容器客户端的工厂。
/// </summary>
public interface IBlobContainerFactory
{
    /// <summary>
    /// 获取应供指定 Grain 使用的容器。
    /// </summary>
    /// <param name="grainId">Grain ID。</param>
    /// <returns>已配置的 Blob 客户端。</returns>
    public BlobContainerClient GetBlobContainerClient(GrainId grainId);

    /// <summary>
    /// 使用提供的客户端和选项初始化所需的依赖项。
    /// </summary>
    /// <param name="client">已连接的 Blob 客户端。</param>
    /// <returns>表示异步操作的 <see cref="Task"/>。</returns>
    public Task InitializeAsync(BlobServiceClient client);
}

/// <summary>
/// 使用默认容器名称的默认 Blob 容器工厂。
/// </summary>
internal class DefaultBlobContainerFactory : IBlobContainerFactory
{
    private readonly AzureBlobStorageOptions _options;
    private BlobContainerClient _defaultContainer = null!;

    /// <summary>
    /// 初始化 <see cref="DefaultBlobContainerFactory"/> 类的新实例。
    /// </summary>
    /// <param name="options">Blob 存储选项。</param>
    public DefaultBlobContainerFactory(AzureBlobStorageOptions options)
    {
        _options = options;
    }

    /// <inheritdoc/>
    public BlobContainerClient GetBlobContainerClient(GrainId grainId)
        => _defaultContainer;

    /// <inheritdoc/>
    public async Task InitializeAsync(BlobServiceClient client)
    {
        _defaultContainer = client.GetBlobContainerClient(_options.ContainerName);
        await _defaultContainer.CreateIfNotExistsAsync();
    }
}
