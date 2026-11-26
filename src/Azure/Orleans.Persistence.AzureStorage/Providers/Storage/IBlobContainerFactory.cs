using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Orleans.Configuration;
using Orleans.Runtime;

namespace Orleans.Storage;

/// <summary>
/// 建造集装箱客户用 grainType 和 grainId储存容器的工厂
/// </summary>
public interface IBlobContainerFactory
{
    /// <summary>
    /// 获取指定 grain所要使用的容器。
    /// </summary>
    /// <param name="grainId">下 grain id</param>
    /// <returns>配置的布束客户端</returns>
    public BlobContainerClient GetBlobContainerClient(GrainId grainId);

    /// <summary>
    /// 利用所提供的客户和选择,启动任何必要的依赖关系。
    /// </summary>
    /// <param name="client">连接的上衣客户端</param>
    /// <returns>A <see cref="Task"/> 代表非同步操作。</returns>
    public Task InitializeAsync(BlobServiceClient client);
}

/// <summary>
/// 使用默认容器名称的默认泡沫集装箱工厂。
/// </summary>
internal class DefaultBlobContainerFactory : IBlobContainerFactory
{
    private readonly AzureBlobStorageOptions _options;
    private BlobContainerClient _defaultContainer = null!;

    /// <summary>
    /// 初始化 <see cref="DefaultBlobContainerFactory"/> 类的新实例。
    /// </summary>
    /// <param name="options">易泡布储存选项</param>
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
