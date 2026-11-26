using System.Diagnostics.CodeAnalysis;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 亚马逊S3日记存储提供者配置选项。
/// </summary>
public sealed class S3JournalStorageOptions
{
    internal const string WalObjectKeyPrefix = "wal/";

    private IAmazonS3? _s3Client;

    /// <summary>
    /// 日记存放的桶名。
    /// </summary>
    public string? BucketName { get; set; }

    /// <summary>
    /// 获取或设置用于生成日记基本对象密钥的代表。
    /// </summary>
    /// <remarks>
    /// 使用 <c>wal/&lt;base-key&gt;</c> 和检查站物体使用的
    /// <c>checkpoints/&lt;base-key&gt;/&lt;snapshot-id&gt;</c>。代表不得添加这些命名空间。
    /// </remarks>
    public Func<JournalId, string> GetObjectKey { get; set; } = DefaultGetObjectKey;

    /// <summary>
    /// 获取或设置代表对基础对象密钥前缀的无默认目录前缀进行映射。
    /// </summary>
    /// <remarks>
    /// 结果必须是非空的, 并包括每个日记的底目标键, 其编号
    /// 以提供的原始正则前缀(包括部分段)开始。
    /// 被目录过滤。如果未设置,默认身份 <see cref="GetObjectKey"/>
    /// 映射使用原始日记前缀。 自定义对象密钥映射必须配置此代表
    /// 使用前置标签。未预设的列表不需要该代表。
    /// 提供者将 <c>wal/</c> 作为结果的预置;代表不得添加。
    /// <see cref="UseOrderedListing"/> 假时, 绘制的前缀会宽到最近的
    /// 支持目录桶的斜线终止目录边界,同时保留 <c>wal/</c>。
    /// </remarks>
    public Func<JournalId, string>? GetObjectKeyPrefix { get; set; }

    /// <summary>
    /// 获取或设定值,表明桶的用法支持按顺序排列
    /// <see cref="Amazon.S3.Model.ListObjectsV2Request"/> 列表。 默认为假 。
    /// </summary>
    /// <remarks>
    /// 仅对保证订购的通用桶或兼容服务启用此功能
    /// 列表和支持 <see cref="Amazon.S3.Model.ListObjectsV2Request.StartAfter"/>。
    /// 目录桶没有顺序, 必须禁用此功能。 默认身份
    /// 物体-钥匙绘图、定序列表可寻求ASCII低约束下方的
    /// ASCII 上界。 自定义绘图不使用本地的密钥区域界限 。
    /// 这一能力独立于 <see cref="UseS3ExpressAppend"/>。
    /// </remarks>
    public bool UseOrderedListing { get; set; }

    /// <summary>
    /// 从基对象密钥获取或设置用于分析日记代号的代表。
    /// </summary>
    /// <remarks>名表在援引该代表之前删除了 <c>wal/</c> 命名空间。</remarks>
    public Func<string, JournalId?> TryParseJournalId { get; set; } = DefaultTryParseJournalId;

    /// <summary>
    /// 创建 S3 客户端时使用选项, 或者 <see langword="null"/> 使用 AWS SDK 默认值时使用选项。
    /// </summary>
    public AmazonS3Config? ClientConfig { get; set; }

    /// <summary>
    /// 获取或设置用于访问 S3 的客户端。
    /// </summary>
    /// <remarks>
    /// 调用者保留该客户的拥有权,并在 silo 停止后处置。
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
    /// 获取或设定一个值,显示如果配置的桶不存在是否应该创建。
    /// </summary>
    /// <remarks>
    /// AWS S3 Express One区目录桶一般应提前提供。
    /// </remarks>
    public bool CreateBucketIfNotExists { get; set; }

    /// <summary>
    /// 获取或设置适用于新建WAL和检查站物体的S3储存类。
    /// </summary>
    public S3StorageClass? StorageClass { get; set; } = S3StorageClass.ExpressOnezone;

    /// <summary>
    /// 获取或设置一个值,以显示附件是否使用 S3 Express <see cref="Amazon.S3.Model.PutObjectRequest.WriteOffsetBytes"/>。
    /// </summary>
    /// <remarks>
    /// 设置此为 <see langword="false"/> , 使用带有 S3 兼容服务( 如 SeaweedFS) 的有条件重写附件 。
    /// </remarks>
    public bool UseS3ExpressAppend { get; set; } = true;

    /// <summary>
    /// 查找或设定值,以显示删除请求是否添加 S3 Express 大小和最后修改的条件
    /// 除了WAL ETag条件之外。
    /// </summary>
    /// <remarks>设置此为S3兼容模拟器的 <see langword="false"/> ,该模拟器仅支持ETag条件。</remarks>
    public bool UseConditionalDelete { get; set; } = true;

    /// <summary>
    /// 获取或设定一个值,显示是否在新检查站公布后删除过时的检查站对象。默认为真 。
    /// </summary>
    public bool DeleteOldCheckpoints { get; set; } = true;

    /// <summary>
    /// 获取或设定最大次数的次数 附加、替换或删除将刷新其缓存 WAL ETag
    /// 并重试在观察仅使用元数据的冲突后。 默认为 5 。
    /// </summary>
    public int MaxMetadataOnlyConflictRetries { get; set; } = DEFAULT_MAX_METADATA_ONLY_CONFLICT_RETRIES;

    /// <summary>
    /// 仅使用元数据冲突重试的默认最大值。
    /// </summary>
    public const int DEFAULT_MAX_METADATA_ONLY_CONFLICT_RETRIES = 5;

    /// <summary>
    /// 获取或设置在仅使用元数据的冲突发生后重试前应用的初始延迟。默认为 10 毫秒。
    /// </summary>
    public TimeSpan MetadataOnlyConflictInitialBackoff { get; set; } = DEFAULT_METADATA_ONLY_CONFLICT_INITIAL_BACKOFF;

    /// <summary>
    /// 重试仅使用元数据的冲突前, 默认初始延迟应用到它 。
    /// </summary>
    public static readonly TimeSpan DEFAULT_METADATA_ONLY_CONFLICT_INITIAL_BACKOFF = TimeSpan.FromMilliseconds(10);

    /// <summary>
    /// 获取或设置仅使用元数据的冲突框的上限, 重试回溯。 默认值为 200 ms 。
    /// </summary>
    public TimeSpan MetadataOnlyConflictMaxBackoff { get; set; } = DEFAULT_METADATA_ONLY_CONFLICT_MAX_BACKOFF;

    /// <summary>
    /// 仅用于元数据的冲突后再试时的默认上限。
    /// </summary>
    public static readonly TimeSpan DEFAULT_METADATA_ONLY_CONFLICT_MAX_BACKOFF = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// 用于创建 <see cref="IAmazonS3"/> 实例的可选代表 。
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
    /// 使用所提供的回调配置 S3 客户端 。
    /// </summary>
    /// <remarks>
    /// 供应商拥有并处置回调后返回的客户。
    /// </remarks>
    public void ConfigureS3Client(Func<CancellationToken, Task<IAmazonS3>> createClientCallback)
    {
        CreateClient = createClientCallback ?? throw new ArgumentNullException(nameof(createClientCallback));
        _s3Client = null;
        IsClientExternallyOwned = false;
    }

    /// <summary>
    /// 配置 S3 客户端, 使用 AWS SDK 默认的证书链和所提供的客户端配置 。
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
    /// 使用 AWS SDK 默认证书链和提供区域配置 S3 客户端。
    /// </summary>
    public void ConfigureS3Client(RegionEndpoint regionEndpoint)
    {
        ArgumentNullException.ThrowIfNull(regionEndpoint);
        ConfigureS3Client(new AmazonS3Config { RegionEndpoint = regionEndpoint });
    }

    /// <summary>
    /// 使用明确的证书和客户配置配置配置配置 S3 客户端 。
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
