using Azure.Storage.Blobs;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 使用默认容器名称的默认 Blob 容器工厂。
/// </summary>
/// <remarks>
/// 初始化 <see cref="DefaultBlobContainerFactory"/> 类的新实例。
/// </remarks>
/// <param name="options">Blob 存储选项。</param>
internal sealed class DefaultBlobContainerFactory(AzureBlobJournalStorageOptions options) : IBlobContainerFactory
{
    private BlobContainerClient _defaultContainer = null!;

    /// <inheritdoc/>
    public BlobContainerClient GetBlobContainerClient(JournalId journalId)
    {
        if (journalId.IsDefault)
        {
            throw new ArgumentException("The journal id must not be the default value.", nameof(journalId));
        }

        return _defaultContainer;
    }

    /// <inheritdoc/>
    public async Task InitializeAsync(BlobServiceClient client, CancellationToken cancellationToken)
    {
        _defaultContainer = client.GetBlobContainerClient(options.ContainerName);
        await _defaultContainer.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
    }
}
