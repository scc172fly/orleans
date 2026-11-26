using Azure;
using Azure.Core;
using Azure.Storage;
using Azure.Storage.Blobs;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 配置 Azure 布束存储器选项。
/// </summary>
public sealed class AzureBlobJournalStorageOptions
{
    /// <summary>
    /// 查找或设置存储日记数据容器的名称。
    /// </summary>
    public string ContainerName { get; set; } = DEFAULT_CONTAINER_NAME;

    /// <summary>
    /// 日记数据的默认容器名称 。
    /// </summary>
    public const string DEFAULT_CONTAINER_NAME = "state";

    /// <summary>
    /// 获取或设置用于生成日记日志的写头对数布号的代表名称。
    /// </summary>
    /// <remarks>
    /// 默认名称为 <c>wal/{journalId.Value}</c>。
    /// <see cref="ContainerName"/>. 参加目录查找的海关代表必须
    /// 每个日记id 都产生相同的名称。
    /// </remarks>
    public Func<JournalId, string> GetWalBlobName { get; set; } = DefaultGetWalBlobName;

    private static readonly Func<JournalId, string> DefaultGetWalBlobName =
        static journalId => GetDefaultWalBlobName(journalId);

    /// <summary>
    /// 获取或设置用于生成日记快照的检查点布条名称的代表。
    /// </summary>
    /// <remarks>
    /// 代表收到日记号,并收到为检查站制作的不透明快照号。
    /// 快速描述id目前格式化为32个字符十六进制字符串,使用 <c>Guid.ToString("N")</c>。
    /// 返回的值必须是容器相对浮点点名称。默认值是
    /// <c>checkpoints/{journalId.Value}/{snapshotId}</c>.
    /// </remarks>
    public Func<JournalId, string, string> GetCheckpointBlobName { get; set; } = DefaultGetCheckpointBlobName;

    private static readonly Func<JournalId, string, string> DefaultGetCheckpointBlobName =
        static (journalId, snapshotId) => GetDefaultCheckpointBlobName(journalId, snapshotId);

    /// <summary>
    /// 当配置 blob 存储客户端时, 或者 <see langword="null"/> 使用默认选项时要使用的选项 。
    /// </summary>
    public BlobClientOptions? ClientOptions { get; set; }

    /// <summary>
    /// 获取或设置用于访问 Azure 球形服务的客户端。
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
    /// 获取或设置值,以显示是否在新检查站公布后删除了过时的检查点布。默认为真 。
    /// </summary>
    public bool DeleteOldCheckpoints { get; set; } = true;

    /// <summary>
    /// 获取或设定日记附加、替换或删除操作的最大次数
    /// 将更新其隐藏的 WAL ETag ,并在观测到仅使用元数据 ETag 后重试
    /// 冲突(例如,同时使用自有的调用元数据更新或生成峰值)。
    /// 当上限超过上限时,存储层会回落到抛掷
    /// <see cref="Orleans.Storage.InconsistentStateException"/> 和日记层恢复
    /// 重试前的默认值为 5。
    /// </summary>
    public int MaxMetadataOnlyConflictRetries { get; set; } = DEFAULT_MAX_METADATA_ONLY_CONFLICT_RETRIES;

    /// <summary>
    /// 仅使用元数据冲突重试的默认最大值。
    /// </summary>
    public const int DEFAULT_MAX_METADATA_ONLY_CONFLICT_RETRIES = 5;

    /// <summary>
    /// 获取或设置在只使用元数据ETag冲突后重新尝试前应用的初始延迟。
    /// 随后的尝试将延误增加一倍(最高为 <see cref="MetadataOnlyConflictMaxBackoff"/>)。
    /// 设为 <see cref="TimeSpan.Zero"/> , 将立即重试而不回落。 默认为 10 毫秒 。
    /// </summary>
    public TimeSpan MetadataOnlyConflictInitialBackoff { get; set; } = DEFAULT_METADATA_ONLY_CONFLICT_INITIAL_BACKOFF;

    /// <summary>
    /// 仅使用元数据冲突重试的默认初始延迟 。
    /// </summary>
    public static readonly TimeSpan DEFAULT_METADATA_ONLY_CONFLICT_INITIAL_BACKOFF = TimeSpan.FromMilliseconds(10);

    /// <summary>
    /// 获取或设定仅用于元数据冲突的每次尝试回卸的上限
    /// 重试。指数时间表从 <see cref="MetadataOnlyConflictInitialBackoff"/>开始
    /// 且从未超过此值。默认为 200 ms。
    /// </summary>
    public TimeSpan MetadataOnlyConflictMaxBackoff { get; set; } = DEFAULT_METADATA_ONLY_CONFLICT_MAX_BACKOFF;

    /// <summary>
    /// 仅使用元数据冲突重试的默认最大延迟 。
    /// </summary>
    public static readonly TimeSpan DEFAULT_METADATA_ONLY_CONFLICT_MAX_BACKOFF = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// 用于创建 <see cref="BlobServiceClient"/> 实例的可选代表 。
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
    /// 用于建造集装箱工厂的功能。
    /// </summary>
    public Func<IServiceProvider, AzureBlobJournalStorageOptions, IBlobContainerFactory> BuildContainerFactory { get; set; }
        = static (provider, options) => new DefaultBlobContainerFactory(options);

    /// <summary>
    /// 使用连接字符串配置 <see cref="BlobServiceClient"/> 。
    /// </summary>
    public void ConfigureBlobServiceClient(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        CreateClient = ct => Task.FromResult(new BlobServiceClient(connectionString, ClientOptions));
    }

    /// <summary>
    /// 使用验证的服务 URI 配置 <see cref="BlobServiceClient"/> 。
    /// </summary>
    public void ConfigureBlobServiceClient(Uri serviceUri)
    {
        ArgumentNullException.ThrowIfNull(serviceUri);
        CreateClient = ct => Task.FromResult(new BlobServiceClient(serviceUri, ClientOptions));
    }

    /// <summary>
    /// 使用所提供的回调配置 <see cref="BlobServiceClient"/> 。
    /// </summary>
    public void ConfigureBlobServiceClient(Func<CancellationToken, Task<BlobServiceClient>> createClientCallback)
    {
        CreateClient = createClientCallback ?? throw new ArgumentNullException(nameof(createClientCallback));
    }

    /// <summary>
    /// 配置 <see cref="BlobServiceClient"/> 使用认证服务 URI 和 a <see cref="TokenCredential"/>.
    /// </summary>
    public void ConfigureBlobServiceClient(Uri serviceUri, TokenCredential tokenCredential)
    {
        ArgumentNullException.ThrowIfNull(serviceUri);
        ArgumentNullException.ThrowIfNull(tokenCredential);
        CreateClient = ct => Task.FromResult(new BlobServiceClient(serviceUri, tokenCredential, ClientOptions));
    }

    /// <summary>
    /// 配置 <see cref="BlobServiceClient"/> 使用认证服务 URI 和 a <see cref="AzureSasCredential"/>.
    /// </summary>
    public void ConfigureBlobServiceClient(Uri serviceUri, AzureSasCredential azureSasCredential)
    {
        ArgumentNullException.ThrowIfNull(serviceUri);
        ArgumentNullException.ThrowIfNull(azureSasCredential);
        CreateClient = ct => Task.FromResult(new BlobServiceClient(serviceUri, azureSasCredential, ClientOptions));
    }

    /// <summary>
    /// 配置 <see cref="BlobServiceClient"/> 使用认证服务 URI 和 a <see cref="StorageSharedKeyCredential"/>.
    /// </summary>
    public void ConfigureBlobServiceClient(Uri serviceUri, StorageSharedKeyCredential sharedKeyCredential)
    {
        ArgumentNullException.ThrowIfNull(serviceUri);
        ArgumentNullException.ThrowIfNull(sharedKeyCredential);
        CreateClient = ct => Task.FromResult(new BlobServiceClient(serviceUri, sharedKeyCredential, ClientOptions));
    }
}
