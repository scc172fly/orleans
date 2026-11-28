using Azure.Storage.Blobs;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 用于构建 Blob Storage 容器客户端的工厂。
/// </summary>
public interface IBlobContainerFactory
{
    /// <summary>
    /// 获取应供指定日志使用的容器。
    /// </summary>
    /// <param name="journalId">日志 ID。</param>
    /// <returns>已配置的 Blob 客户端。</returns>
    public BlobContainerClient GetBlobContainerClient(JournalId journalId);

    /// <summary>
    /// 使用提供的客户端和选项初始化所需的依赖项。
    /// </summary>
    /// <param name="client">已连接的 Blob 客户端。</param>
    /// <param name="cancellationToken">用于取消请求的令牌。</param>
    /// <returns>表示异步操作的 <see cref="Task"/>。</returns>
    public Task InitializeAsync(BlobServiceClient client, CancellationToken cancellationToken);
}
