using Azure;
using Azure.Core;
using Azure.Data.Tables;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 配置 Azure 表日记账存储提供者的选项。
/// </summary>
public sealed class AzureTableJournalStorageOptions
{
    /// <summary>
    /// 存储日记数据表名称。
    /// </summary>
    public string TableName { get; set; } = DEFAULT_TABLE_NAME;

    /// <summary>
    /// 日记数据的默认表格名称。
    /// </summary>
    public const string DEFAULT_TABLE_NAME = "journal";

    /// <summary>
    /// 获取或设置用于生成日记时钟表格分区密钥的代表。
    /// </summary>
    /// <remarks>
    /// 返回的值必须是有效的 Azure 表格分区密钥。默认映射只接受
    /// <see cref="JournalId.Value"/> 中可打印 ASCII 字符(U+0020至 U+0007E),并编码
    /// 字节作为两个大写十六进制数字, 保存正则顺序和原始前缀。
    /// 最多512个字符的1 024个字码分隔键允许使用日记代号。
    /// 自定义绘图不受默认制图日记识别限制和使用卡通
    /// 用于目录查询的日记属性过滤器,这需要全表扫描而不是
    /// 索引化分区密钥区域。目录界限不需要满足存储的日记号限制。
    /// </remarks>
    public Func<JournalId, string> GetPartitionKey { get; set; } = DefaultGetPartitionKey;

    private static readonly Func<JournalId, string> DefaultGetPartitionKey =
        static journalId => GetDefaultPartitionKey(journalId);

    internal bool UsesDefaultPartitionKey => GetPartitionKey == DefaultGetPartitionKey;

    /// <summary>
    /// 配置表格存储客户端时,或使用默认选项时使用的选项,或者 <see langword="null"/> 。
    /// </summary>
    public TableClientOptions? ClientOptions { get; set; }

    /// <summary>
    /// 获取或设置用于访问 Azure 表服务的客户端。
    /// </summary>
    public TableServiceClient? TableServiceClient
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
    /// 获取或设置值,以显示是否删除属于前一个日记生成的行
    /// 替换操作后发布新一代。 默认为真 。
    /// </summary>
    public bool DeleteOldGenerations { get; set; } = true;

    /// <summary>
    /// 获取或设置自上次替换操作以来写入的数据行数
    /// <see cref="IJournalStorage.IsCompactionRequested"/> 成为事实。默认为10,000。
    /// </summary>
    public long CompactionRowCountThreshold { get; set; } = DEFAULT_COMPACTION_ROW_COUNT_THRESHOLD;

    /// <summary>
    /// 需要压缩的日记行默认数 。
    /// </summary>
    public const long DEFAULT_COMPACTION_ROW_COUNT_THRESHOLD = 10_000;

    /// <summary>
    /// 获取或设置自上次替换操作以来所写的日记字节数
    /// <see cref="IJournalStorage.IsCompactionRequested"/> 成为真。默认为 32 MiB。
    /// </summary>
    public long CompactionSizeThreshold { get; set; } = DEFAULT_COMPACTION_SIZE_THRESHOLD;

    /// <summary>
    /// 需要压缩的日记位数默认值。
    /// </summary>
    public const long DEFAULT_COMPACTION_SIZE_THRESHOLD = 32L * 1024 * 1024;

    /// <summary>
    /// 获取或设定日记附加、替换或删除操作的最大次数
    /// 将更新其缓存头头目录ETag, 并在观测仅元数据ETag后重试
    /// 冲突(例如,同时使用自有的调用元数据更新)。
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
    /// 用于创建 <see cref="TableServiceClient"/> 实例的可选代表 。
    /// </summary>
    internal Func<CancellationToken, Task<TableServiceClient>>? CreateClient { get; private set; }

    internal string GetPartitionKeyForJournal(JournalId journalId)
    {
        if (journalId.IsDefault)
        {
            throw new ArgumentException("The journal id must not be the default value.", nameof(journalId));
        }

        var mapper = GetPartitionKey ?? throw new InvalidOperationException("A partition key mapper must be configured.");
        var partitionKey = mapper(journalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(partitionKey);
        ValidatePartitionKey(partitionKey, nameof(partitionKey));
        return partitionKey;
    }

    internal static string GetDefaultPartitionKey(JournalId journalId)
    {
        if (journalId.IsDefault)
        {
            throw new ArgumentException("The journal id must not be the default value.", nameof(journalId));
        }

        if (journalId.Value.Length > 512)
        {
            throw new ArgumentException(
                "Azure Table partition keys must not exceed 1,024 characters.",
                nameof(journalId));
        }

        if (journalId.Value.AsSpan().IndexOfAnyExceptInRange(' ', '~') >= 0)
        {
            throw new ArgumentException(
                "The default Azure Table partition key mapping requires journal ids to contain only printable ASCII characters (U+0020 through U+007E).",
                nameof(journalId));
        }

        var partitionKey = EncodePartitionKey(journalId.Value);
        ValidatePartitionKey(partitionKey, nameof(journalId));
        return partitionKey;
    }

    internal static string EncodePartitionKey(string value)
        => string.Create(checked(value.Length * 2), value, static (destination, source) =>
        {
            const string HexDigits = "0123456789ABCDEF";
            for (var index = 0; index < source.Length; index++)
            {
                var character = source[index];
                var offset = index * 2;
                destination[offset] = HexDigits[character >> 4];
                destination[offset + 1] = HexDigits[character & 0xF];
            }
        });

    internal static void ValidateTableName(string? tableName)
    {
        if (tableName is not { Length: >= 3 and <= 63 } || !IsAsciiLetter(tableName[0]))
        {
#pragma warning disable CA2208 // Validation reports the public configuration property rather than this helper parameter.
            throw new ArgumentException(
                "Azure Table names must contain 3 to 63 alphanumeric characters and begin with a letter.",
                nameof(TableName));
#pragma warning restore CA2208
        }

        foreach (var character in tableName)
        {
            if (!IsAsciiLetter(character) && character is not (>= '0' and <= '9'))
            {
#pragma warning disable CA2208 // Validation reports the public configuration property rather than this helper parameter.
                throw new ArgumentException(
                    "Azure Table names must contain 3 to 63 alphanumeric characters and begin with a letter.",
                    nameof(TableName));
#pragma warning restore CA2208
            }
        }
    }

    private static void ValidatePartitionKey(string partitionKey, string parameterName)
    {
        if (partitionKey.Length > 1024)
        {
            throw new ArgumentException(
                "Azure Table partition keys must not exceed 1,024 characters.",
                parameterName);
        }

        foreach (var character in partitionKey)
        {
            if (character is '/' or '\\' or '#' or '?'
                or >= '\u0000' and <= '\u001F'
                or >= '\u007F' and <= '\u009F')
            {
                throw new ArgumentException(
                    "Azure Table partition keys must not contain '/', '\\', '#', '?', or control characters.",
                    parameterName);
            }
        }
    }

    private static bool IsAsciiLetter(char character)
        => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    /// <summary>
    /// 使用连接字符串配置 <see cref="TableServiceClient"/> 。
    /// </summary>
    public void ConfigureTableServiceClient(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        CreateClient = ct => Task.FromResult(new TableServiceClient(connectionString, ClientOptions));
    }

    /// <summary>
    /// 使用验证的服务 URI 配置 <see cref="TableServiceClient"/> 。
    /// </summary>
    public void ConfigureTableServiceClient(Uri serviceUri)
    {
        ArgumentNullException.ThrowIfNull(serviceUri);
        CreateClient = ct => Task.FromResult(new TableServiceClient(serviceUri, ClientOptions));
    }

    /// <summary>
    /// 使用所提供的回调配置 <see cref="TableServiceClient"/> 。
    /// </summary>
    public void ConfigureTableServiceClient(Func<CancellationToken, Task<TableServiceClient>> createClientCallback)
    {
        CreateClient = createClientCallback ?? throw new ArgumentNullException(nameof(createClientCallback));
    }

    /// <summary>
    /// 配置 <see cref="TableServiceClient"/> 使用认证服务 URI 和 a <see cref="TokenCredential"/>.
    /// </summary>
    public void ConfigureTableServiceClient(Uri serviceUri, TokenCredential tokenCredential)
    {
        ArgumentNullException.ThrowIfNull(serviceUri);
        ArgumentNullException.ThrowIfNull(tokenCredential);
        CreateClient = ct => Task.FromResult(new TableServiceClient(serviceUri, tokenCredential, ClientOptions));
    }

    /// <summary>
    /// 配置 <see cref="TableServiceClient"/> 使用认证服务 URI 和 a <see cref="AzureSasCredential"/>.
    /// </summary>
    public void ConfigureTableServiceClient(Uri serviceUri, AzureSasCredential azureSasCredential)
    {
        ArgumentNullException.ThrowIfNull(serviceUri);
        ArgumentNullException.ThrowIfNull(azureSasCredential);
        CreateClient = ct => Task.FromResult(new TableServiceClient(serviceUri, azureSasCredential, ClientOptions));
    }

    /// <summary>
    /// 配置 <see cref="TableServiceClient"/> 使用认证服务 URI 和 a <see cref="TableSharedKeyCredential"/>.
    /// </summary>
    public void ConfigureTableServiceClient(Uri serviceUri, TableSharedKeyCredential sharedKeyCredential)
    {
        ArgumentNullException.ThrowIfNull(serviceUri);
        ArgumentNullException.ThrowIfNull(sharedKeyCredential);
        CreateClient = ct => Task.FromResult(new TableServiceClient(serviceUri, sharedKeyCredential, ClientOptions));
    }
}
