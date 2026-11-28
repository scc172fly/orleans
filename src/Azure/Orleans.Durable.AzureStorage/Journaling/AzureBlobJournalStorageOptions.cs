using Azure;
using Azure.Core;
using Azure.Storage;
using Azure.Storage.Blobs;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 用于配置 Azure Blob 日志存储提供程序的选项。
/// </summary>
public sealed class AzureBlobJournalStorageOptions
{
    /// <summary>
    /// 获取或设置存储日志数据的容器名称。
    /// </summary>
    public string ContainerName { get; set; } = DEFAULT_CONTAINER_NAME;

    /// <summary>
    /// 日志数据容器的默认名称。
    /// </summary>
    public const string DEFAULT_CONTAINER_NAME = "state";

    /// <summary>
    /// 获取或设置为日志生成写前日志 Blob 名称的委托。
    /// </summary>
    /// <remarks>
    /// 默认名称为 <c>wal/{journalId.Value}</c>。目录发现在
    /// <see cref="ContainerName"/> 指定的容器中使用此布局。参与目录发现的自定义委托必须
    /// 为每个日志 ID 生成与默认委托相同的名称。
    /// </remarks>
    public Func<JournalId, string> GetWalBlobName { get; set; } = DefaultGetWalBlobName;

    private static readonly Func<JournalId, string> DefaultGetWalBlobName =
        static journalId => GetDefaultWalBlobName(journalId);

    /// <summary>
    /// 获取或设置为日志快照生成检查点 Blob 名称的委托。
    /// </summary>
    /// <remarks>
    /// 委托接收日志 ID 及为检查点生成的不透明快照 ID。
    /// 目前快照 ID 使用 <c>Guid.ToString("N")</c> 格式化为 32 字符十六进制字符串。
    /// 返回值必须是相对于容器的 Blob 名称。默认值为
    /// <c>checkpoints/{journalId.Value}/{snapshotId}</c>。
    /// </remarks>
    public Func<JournalId, string, string> GetCheckpointBlobName { get; set; } = DefaultGetCheckpointBlobName;

    private static readonly Func<JournalId, string, string> DefaultGetCheckpointBlobName =
        static (journalId, snapshotId) => GetDefaultCheckpointBlobName(journalId, snapshotId);

    /// <summary>
    /// 配置 Blob Storage 客户端时使用的选项，或使用 <see langword="null"/> 采用默认选项。
    /// </summary>
    public BlobClientOptions? ClientOptions { get; set; }

    /// <summary>
    /// 获取或设置用于访问 Azure Blob 服务的客户端。
    /// </summary>
    public BlobServiceClient? BlobServiceClient
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
            CreateClient = ct => Task.FromResult(value);
        }
    }

    /// <summary>
    /// 获取或设置一个值，指示发布新检查点后是否删除过时检查点 Blob。默认为 true。
    /// </summary>
    public bool DeleteOldCheckpoints { get; set; } = true;

    /// <summary>
    /// 获取或设置日志 Append、Replace 或 Delete 操作在
    /// 观测到仅元数据导致的 ETag 冲突后刷新缓存 WAL ETag 并就地重试的最大次数，
    /// 例如调用方所有的元数据发生并发更新或世代编号递增。
    /// 超出上限时，存储层回退为抛出
    /// <see cref="Orleans.Storage.InconsistentStateException"/>，日志层在
    /// 重试前执行恢复。默认为 5。
    /// </summary>
    public int MaxMetadataOnlyConflictRetries { get; set; } = DEFAULT_MAX_METADATA_ONLY_CONFLICT_RETRIES;

    /// <summary>
    /// 仅元数据冲突的默认最大重试次数。
    /// </summary>
    public const int DEFAULT_MAX_METADATA_ONLY_CONFLICT_RETRIES = 5;

    /// <summary>
    /// 获取或设置发生仅元数据导致的 ETag 冲突后，重试之前应用的初始延迟。
    /// 后续尝试使延迟翻倍（上限为 <see cref="MetadataOnlyConflictMaxBackoff"/>）。
    /// 设置为 <see cref="TimeSpan.Zero"/> 可立即重试而不进行退避。默认为 10 ms。
    /// </summary>
    public TimeSpan MetadataOnlyConflictInitialBackoff { get; set; } = DEFAULT_METADATA_ONLY_CONFLICT_INITIAL_BACKOFF;

    /// <summary>
    /// 仅元数据冲突重试的默认初始延迟。
    /// </summary>
    public static readonly TimeSpan DEFAULT_METADATA_ONLY_CONFLICT_INITIAL_BACKOFF = TimeSpan.FromMilliseconds(10);

    /// <summary>
    /// 获取或设置仅元数据冲突
    /// 重试每次尝试的退避时间上限。指数退避从 <see cref="MetadataOnlyConflictInitialBackoff"/> 开始，
    /// 始终不超过此值。默认为 200 ms。
    /// </summary>
    public TimeSpan MetadataOnlyConflictMaxBackoff { get; set; } = DEFAULT_METADATA_ONLY_CONFLICT_MAX_BACKOFF;

    /// <summary>
    /// 仅元数据冲突重试之间的默认最大延迟。
    /// </summary>
    public static readonly TimeSpan DEFAULT_METADATA_ONLY_CONFLICT_MAX_BACKOFF = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// 用于创建 <see cref="BlobServiceClient"/> 实例的可选委托。
    /// </summary>
    internal Func<CancellationToken, Task<BlobServiceClient>>? CreateClient { get; private set; }

    internal string GetWalBlobNameForJournal(JournalId journalId)
    {
        if (journalId.IsDefault)
        {
            throw new ArgumentException("The journal id must not be the default value.", nameof(journalId));
        }

        var blobName = GetWalBlobName(journalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(blobName);
        return blobName;
    }

    internal string GetCheckpointBlobNameForJournal(JournalId journalId, string snapshotId)
    {
        if (journalId.IsDefault)
        {
            throw new ArgumentException("The journal id must not be the default value.", nameof(journalId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotId);
        var blobName = GetCheckpointBlobName(journalId, snapshotId);
        ArgumentException.ThrowIfNullOrWhiteSpace(blobName);
        return blobName;
    }

    internal static string GetDefaultWalBlobName(JournalId journalId)
    {
        if (journalId.IsDefault)
        {
            throw new ArgumentException("The journal id must not be the default value.", nameof(journalId));
        }

        return AzureBlobJournalStorageLayout.GetWalBlobName(journalId.Value);
    }

    internal static string GetDefaultCheckpointBlobName(JournalId journalId, string snapshotId)
    {
        if (journalId.IsDefault)
        {
            throw new ArgumentException("The journal id must not be the default value.", nameof(journalId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotId);
        return AzureBlobJournalStorageLayout.GetCheckpointBlobName(journalId.Value, snapshotId);
    }

    /// <summary>
    /// 用于构建容器工厂实例的函数。
    /// </summary>
    public Func<IServiceProvider, AzureBlobJournalStorageOptions, IBlobContainerFactory> BuildContainerFactory { get; set; }
        = static (provider, options) => new DefaultBlobContainerFactory(options);

    /// <summary>
    /// 使用连接字符串配置 <see cref="BlobServiceClient"/>。
    /// </summary>
    public void ConfigureBlobServiceClient(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        CreateClient = ct => Task.FromResult(new BlobServiceClient(connectionString, ClientOptions));
    }

    /// <summary>
    /// 使用经过身份验证的服务 URI 配置 <see cref="BlobServiceClient"/>。
    /// </summary>
    public void ConfigureBlobServiceClient(Uri serviceUri)
    {
        ArgumentNullException.ThrowIfNull(serviceUri);
        CreateClient = ct => Task.FromResult(new BlobServiceClient(serviceUri, ClientOptions));
    }

    /// <summary>
    /// 使用提供的回调配置 <see cref="BlobServiceClient"/>。
    /// </summary>
    public void ConfigureBlobServiceClient(Func<CancellationToken, Task<BlobServiceClient>> createClientCallback)
    {
        CreateClient = createClientCallback ?? throw new ArgumentNullException(nameof(createClientCallback));
    }

    /// <summary>
    /// 配置 <see cref="BlobServiceClient"/>，使用经过身份验证的服务 URI 和 <see cref="TokenCredential"/>。
    /// </summary>
    public void ConfigureBlobServiceClient(Uri serviceUri, TokenCredential tokenCredential)
    {
        ArgumentNullException.ThrowIfNull(serviceUri);
        ArgumentNullException.ThrowIfNull(tokenCredential);
        CreateClient = ct => Task.FromResult(new BlobServiceClient(serviceUri, tokenCredential, ClientOptions));
    }

    /// <summary>
    /// 配置 <see cref="BlobServiceClient"/>，使用经过身份验证的服务 URI 和 <see cref="AzureSasCredential"/>。
    /// </summary>
    public void ConfigureBlobServiceClient(Uri serviceUri, AzureSasCredential azureSasCredential)
    {
        ArgumentNullException.ThrowIfNull(serviceUri);
        ArgumentNullException.ThrowIfNull(azureSasCredential);
        CreateClient = ct => Task.FromResult(new BlobServiceClient(serviceUri, azureSasCredential, ClientOptions));
    }

    /// <summary>
    /// 配置 <see cref="BlobServiceClient"/>，使用经过身份验证的服务 URI 和 <see cref="StorageSharedKeyCredential"/>。
    /// </summary>
    public void ConfigureBlobServiceClient(Uri serviceUri, StorageSharedKeyCredential sharedKeyCredential)
    {
        ArgumentNullException.ThrowIfNull(serviceUri);
        ArgumentNullException.ThrowIfNull(sharedKeyCredential);
        CreateClient = ct => Task.FromResult(new BlobServiceClient(serviceUri, sharedKeyCredential, ClientOptions));
    }
}
