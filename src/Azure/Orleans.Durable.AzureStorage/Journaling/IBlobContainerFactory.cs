using Azure.Storage.Blobs;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 一家建造集装箱客户的工厂,用于储存塑料桶。
/// </summary>
public interface IBlobContainerFactory
{
    /// <summary>
    /// 获取指定日记应该使用的容器。
    /// </summary>
    /// <param name="journalId">日记id。</param>
    /// <returns>一个配置的布罗客户端。</returns>
    public BlobContainerClient GetBlobContainerClient(JournalId journalId);

    /// <summary>
    /// 利用所提供的客户和选择,启动任何必要的依赖关系。
    /// </summary>
    /// <param name="client">连接的上衣客户端</param>
    /// <param name="cancellationToken">用于取消请求的标记。</param>
    /// <returns>A <see cref="Task"/> 代表非同步操作。</returns>
    public Task InitializeAsync(BlobServiceClient client, CancellationToken cancellationToken);
}
