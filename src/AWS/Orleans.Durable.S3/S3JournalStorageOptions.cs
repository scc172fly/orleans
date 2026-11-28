using System.Diagnostics.CodeAnalysis;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 用于配置 Amazon S3 日志存储提供程序的选项。
/// </summary>
public sealed class S3JournalStorageOptions
{
    internal const string WalObjectKeyPrefix = "wal/";

    private IAmazonS3? _s3Client;

    /// <summary>
    /// 存储日志的存储桶名称。
    /// </summary>
    public string? BucketName { get; set; }

    /// <summary>
    /// 获取或设置用于生成日志基础对象键的委托。
    /// </summary>
    /// <remarks>
    /// WAL 对象使用 <c>wal/&lt;base-key&gt;</c>，检查点对象使用
    /// <c>checkpoints/&lt;base-key&gt;/&lt;snapshot-id&gt;</c>。委托不得添加这些命名空间前缀。
    /// </remarks>
    public Func<JournalId, string> GetObjectKey { get; set; } = DefaultGetObjectKey;

    /// <summary>
    /// 获取或设置将非默认目录前缀映射为基础对象键前缀的委托。
    /// </summary>
    /// <remarks>
    /// 结果必须非空，并覆盖所有其标识
    /// 以指定的原始序数比较前缀开头的日志的基础对象键，包括不完整的路径段。额外匹配项
    /// 由目录筛除。未设置时，默认的恒等 <see cref="GetObjectKey"/>
    /// 映射使用原始日志前缀。自定义对象键映射必须配置此委托
    /// 才能使用带前缀的列表查询。不带前缀的列表查询不需要此委托。
    /// 提供程序会在结果前添加 <c>wal/</c>；委托不得自行添加。
    /// 当 <see cref="UseOrderedListing"/> 为 false 时，将映射后的前缀扩展到最近的
    /// 以斜杠结尾的目录边界，以支持目录存储桶，同时保留 <c>wal/</c>。
    /// </remarks>
    public Func<JournalId, string>? GetObjectKeyPrefix { get; set; }

    /// <summary>
    /// 获取或设置一个值，指示存储桶是否支持按字典序排列的
    /// <see cref="Amazon.S3.Model.ListObjectsV2Request"/> 列表查询。默认为 false。
    /// </summary>
    /// <remarks>
    /// 仅对保证列表查询有序
    /// 且支持 <see cref="Amazon.S3.Model.ListObjectsV2Request.StartAfter"/> 的通用存储桶或兼容服务启用此选项。
    /// 目录存储桶不保证顺序，必须禁用此选项。使用默认的恒等
    /// 对象键映射时，有序列表查询可以定位到 ASCII 下界，并在超出
    /// ASCII 上界时停止。自定义映射不使用原生键范围边界。
    /// 此能力独立于 <see cref="UseS3ExpressAppend"/>。
    /// </remarks>
    public bool UseOrderedListing { get; set; }

    /// <summary>
    /// 获取或设置用于从基础对象键解析日志标识的委托。
    /// </summary>
    /// <remarks>目录在调用此委托前会移除 <c>wal/</c> 命名空间前缀。</remarks>
    public Func<string, JournalId?> TryParseJournalId { get; set; } = DefaultTryParseJournalId;

    /// <summary>
    /// 创建 S3 客户端时使用的选项；传入 <see langword="null"/> 则使用 AWS SDK 默认配置。
    /// </summary>
    public AmazonS3Config? ClientConfig { get; set; }

    /// <summary>
    /// 获取或设置用于访问 S3 的客户端。
    /// </summary>
    /// <remarks>
    /// 调用方保留此客户端的所有权，并在 Silo 停止后释放它。
    /// </remarks>
    [DisallowNull]
    public IAmazonS3? S3Client
    {
        get => _s3Client;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _s3Client = value;
            CreateClient = _ => Task.FromResult(value);
            IsClientExternallyOwned = true;
        }
    }

    /// <summary>
    /// 获取或设置一个值，指示配置的存储桶不存在时是否创建它。
    /// </summary>
    /// <remarks>
    /// 此选项适用于本地模拟器。AWS S3 Express One Zone 目录存储桶通常应提前预配。
    /// </remarks>
    public bool CreateBucketIfNotExists { get; set; }

    /// <summary>
    /// 获取或设置应用于新建 WAL 和检查点对象的 S3 存储类别。
    /// </summary>
    public S3StorageClass? StorageClass { get; set; } = S3StorageClass.ExpressOnezone;

    /// <summary>
    /// 获取或设置一个值，指示追加操作是否使用 S3 Express <see cref="Amazon.S3.Model.PutObjectRequest.WriteOffsetBytes"/>。
    /// </summary>
    /// <remarks>
    /// 对于 SeaweedFS 等 S3 兼容服务，将此值设为 <see langword="false"/> 以使用条件重写方式追加。
    /// </remarks>
    public bool UseS3ExpressAppend { get; set; } = true;

    /// <summary>
    /// 获取或设置一个值，指示删除请求是否除 WAL ETag 条件之外，
    /// 还添加 S3 Express 的大小和最后修改时间条件。
    /// </summary>
    /// <remarks>对于仅支持 ETag 条件的 S3 兼容模拟器，将此值设为 <see langword="false"/>。</remarks>
    public bool UseConditionalDelete { get; set; } = true;

    /// <summary>
    /// 获取或设置一个值，指示发布新检查点后是否删除过时的检查点对象。默认为 true。
    /// </summary>
    public bool DeleteOldCheckpoints { get; set; } = true;

    /// <summary>
    /// 获取或设置 Append、Replace 或 Delete 在发现仅涉及元数据的冲突后，
    /// 刷新缓存的 WAL ETag 并就地重试的最大次数。默认为 5。
    /// </summary>
    public int MaxMetadataOnlyConflictRetries { get; set; } = DEFAULT_MAX_METADATA_ONLY_CONFLICT_RETRIES;

    /// <summary>
    /// 仅涉及元数据的冲突的默认最大重试次数。
    /// </summary>
    public const int DEFAULT_MAX_METADATA_ONLY_CONFLICT_RETRIES = 5;

    /// <summary>
    /// 获取或设置仅涉及元数据的冲突发生后首次重试前的延迟。默认为 10 ms。
    /// </summary>
    public TimeSpan MetadataOnlyConflictInitialBackoff { get; set; } = DEFAULT_METADATA_ONLY_CONFLICT_INITIAL_BACKOFF;

    /// <summary>
    /// 仅涉及元数据的冲突的默认首次重试延迟。
    /// </summary>
    public static readonly TimeSpan DEFAULT_METADATA_ONLY_CONFLICT_INITIAL_BACKOFF = TimeSpan.FromMilliseconds(10);

    /// <summary>
    /// 获取或设置仅涉及元数据的冲突的重试退避上限。默认为 200 ms。
    /// </summary>
    public TimeSpan MetadataOnlyConflictMaxBackoff { get; set; } = DEFAULT_METADATA_ONLY_CONFLICT_MAX_BACKOFF;

    /// <summary>
    /// 仅涉及元数据的冲突的默认重试退避上限。
    /// </summary>
    public static readonly TimeSpan DEFAULT_METADATA_ONLY_CONFLICT_MAX_BACKOFF = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// 用于创建 <see cref="IAmazonS3"/> 实例的可选委托。
    /// </summary>
    internal Func<CancellationToken, Task<IAmazonS3>>? CreateClient { get; private set; }

    internal bool IsClientExternallyOwned { get; private set; }

    internal bool UsesDefaultObjectKey => GetObjectKey == DefaultGetObjectKey;

    internal string? GetObjectKeyPrefixForCatalog(string? prefix)
    {
        if (prefix is null)
        {
            return null;
        }

        var mapper = GetObjectKeyPrefix;
        if (mapper is null && !UsesDefaultObjectKey)
        {
            throw new InvalidOperationException(
                $"Custom {nameof(GetObjectKey)} mappings require {nameof(GetObjectKeyPrefix)} for prefixed journal listings.");
        }

        var result = mapper is null ? prefix : mapper(new JournalId(prefix));
        if (string.IsNullOrEmpty(result))
        {
            throw new InvalidOperationException(
                $"{nameof(GetObjectKeyPrefix)} must return a non-empty object-key prefix.");
        }

        return result;
    }

    internal string GetObjectKeyForJournal(JournalId journalId)
    {
        if (journalId.IsDefault)
        {
            throw new ArgumentException("The journal id must not be the default value.", nameof(journalId));
        }

        var objectKey = GetObjectKey(journalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        return objectKey;
    }

    internal static string GetWalObjectKeyForJournal(JournalId journalId, string journalObjectKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalObjectKey);
        return GetDefaultWalObjectKey(journalObjectKey);
    }

    internal static string GetCheckpointObjectKeyForJournal(JournalId journalId, string journalObjectKey, string snapshotId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(journalObjectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotId);
        return GetDefaultCheckpointObjectKey(journalObjectKey, snapshotId);
    }

    internal static string GetDefaultWalObjectKey(string journalObjectKey) => $"{WalObjectKeyPrefix}{journalObjectKey}";

    internal static string GetDefaultCheckpointObjectKey(string journalObjectKey, string snapshotId) => $"checkpoints/{journalObjectKey}/{snapshotId}";

    internal Func<CancellationToken, Task<IAmazonS3>> GetCreateClient()
        => CreateClient ?? (_ => Task.FromResult<IAmazonS3>(new AmazonS3Client(ClientConfig ?? new AmazonS3Config())));

    /// <summary>
    /// 使用提供的回调配置 S3 客户端。
    /// </summary>
    /// <remarks>
    /// 提供程序拥有回调返回的客户端，并负责释放它。
    /// </remarks>
    public void ConfigureS3Client(Func<CancellationToken, Task<IAmazonS3>> createClientCallback)
    {
        CreateClient = createClientCallback ?? throw new ArgumentNullException(nameof(createClientCallback));
        _s3Client = null;
        IsClientExternallyOwned = false;
    }

    /// <summary>
    /// 使用 AWS SDK 默认凭据链及提供的客户端配置来配置 S3 客户端。
    /// </summary>
    public void ConfigureS3Client(AmazonS3Config config)
    {
        ArgumentNullException.ThrowIfNull(config);
        ClientConfig = config;
        CreateClient = _ => Task.FromResult<IAmazonS3>(new AmazonS3Client(config));
        _s3Client = null;
        IsClientExternallyOwned = false;
    }

    /// <summary>
    /// 使用 AWS SDK 默认凭据链及提供的区域来配置 S3 客户端。
    /// </summary>
    public void ConfigureS3Client(RegionEndpoint regionEndpoint)
    {
        ArgumentNullException.ThrowIfNull(regionEndpoint);
        ConfigureS3Client(new AmazonS3Config { RegionEndpoint = regionEndpoint });
    }

    /// <summary>
    /// 使用显式凭据和客户端配置来配置 S3 客户端。
    /// </summary>
    public void ConfigureS3Client(string accessKey, string secretKey, AmazonS3Config config)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(secretKey);
        ArgumentNullException.ThrowIfNull(config);
        ClientConfig = config;
        var credentials = new BasicAWSCredentials(accessKey, secretKey);
        CreateClient = _ => Task.FromResult<IAmazonS3>(new AmazonS3Client(credentials, config));
        _s3Client = null;
        IsClientExternallyOwned = false;
    }

    private static string DefaultGetObjectKey(JournalId journalId) => journalId.Value;

    private static JournalId? DefaultTryParseJournalId(string value)
    {
        try
        {
            return new JournalId(value);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
