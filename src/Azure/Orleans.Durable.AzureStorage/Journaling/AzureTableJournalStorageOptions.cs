using Azure;
using Azure.Core;
using Azure.Data.Tables;

namespace Orleans.Durable.Journaling;

/// <summary>
/// 用于配置 Azure Table 日志存储提供程序的选项。
/// </summary>
public sealed class AzureTableJournalStorageOptions
{
    /// <summary>
    /// 存储日志数据的表名。
    /// </summary>
    public string TableName { get; set; } = DEFAULT_TABLE_NAME;

    /// <summary>
    /// 日志数据的默认表名。
    /// </summary>
    public const string DEFAULT_TABLE_NAME = "journal";

    /// <summary>
    /// 获取或设置为日志生成表分区键的委托。
    /// </summary>
    /// <remarks>
    /// 返回值必须是有效的 Azure Table 分区键。默认映射仅接受
    /// <see cref="JournalId.Value"/> 中的可打印 ASCII 字符（U+0020 至 U+007E），并将每个
    /// 字节编码为两个大写十六进制数字，保留按序数比较的顺序及原始前缀。
    /// 1,024 字符的分区键长度限制允许日志 ID 最多包含 512 个字符。
    /// 自定义映射不受默认映射的日志 ID 限制，目录查询使用规范
    /// 日志 ID 属性筛选器，可能需要全表扫描，而非
    /// 利用索引的分区键范围查询。目录查询边界无需满足已存储日志 ID 的限制。
    /// </remarks>
    public Func<JournalId, string> GetPartitionKey { get; set; } = DefaultGetPartitionKey;

    private static readonly Func<JournalId, string> DefaultGetPartitionKey =
        static journalId => GetDefaultPartitionKey(journalId);

    internal bool UsesDefaultPartitionKey => GetPartitionKey == DefaultGetPartitionKey;

    /// <summary>
    /// 配置 Table Storage 客户端时使用的选项，或使用 <see langword="null"/> 采用默认选项。
    /// </summary>
    public TableClientOptions? ClientOptions { get; set; }

    /// <summary>
    /// 获取或设置用于访问 Azure Table 服务的客户端。
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
    /// 获取或设置一个值，指示替换操作发布新世代后是否删除属于上一日志世代的
    /// 数据行。默认为 true。
    /// </summary>
    public bool DeleteOldGenerations { get; set; } = true;

    /// <summary>
    /// 获取或设置自上次替换操作以来写入的数据行数量阈值，达到此值时
    /// <see cref="IJournalStorage.IsCompactionRequested"/> 变为 true。默认为 10,000。
    /// </summary>
    public long CompactionRowCountThreshold { get; set; } = DEFAULT_COMPACTION_ROW_COUNT_THRESHOLD;

    /// <summary>
    /// 触发压实请求的默认日志行数。
    /// </summary>
    public const long DEFAULT_COMPACTION_ROW_COUNT_THRESHOLD = 10_000;

    /// <summary>
    /// 获取或设置自上次替换操作以来写入的日志字节数阈值，达到此值时
    /// <see cref="IJournalStorage.IsCompactionRequested"/> 变为 true。默认为 32 MiB。
    /// </summary>
    public long CompactionSizeThreshold { get; set; } = DEFAULT_COMPACTION_SIZE_THRESHOLD;

    /// <summary>
    /// 触发压实请求的默认日志字节数。
    /// </summary>
    public const long DEFAULT_COMPACTION_SIZE_THRESHOLD = 32L * 1024 * 1024;

    /// <summary>
    /// 获取或设置日志 Append、Replace 或 Delete 操作在
    /// 观测到仅元数据导致的 ETag 冲突后刷新缓存头部行 ETag 并就地重试的最大次数，
    /// 例如调用方所有的元数据发生并发更新。
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
    /// 用于创建 <see cref="TableServiceClient"/> 实例的可选委托。
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
#pragma warning disable CA2208 // 验证错误报告公共配置属性，而不是此辅助方法的参数。
            throw new ArgumentException(
                "Azure Table names must contain 3 to 63 alphanumeric characters and begin with a letter.",
                nameof(TableName));
#pragma warning restore CA2208
        }

        foreach (var character in tableName)
        {
            if (!IsAsciiLetter(character) && character is not (>= '0' and <= '9'))
            {
#pragma warning disable CA2208 // 验证错误报告公共配置属性，而不是此辅助方法的参数。
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
    /// 使用连接字符串配置 <see cref="TableServiceClient"/>。
    /// </summary>
    public void ConfigureTableServiceClient(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        CreateClient = ct => Task.FromResult(new TableServiceClient(connectionString, ClientOptions));
    }

    /// <summary>
    /// 使用经过身份验证的服务 URI 配置 <see cref="TableServiceClient"/>。
    /// </summary>
    public void ConfigureTableServiceClient(Uri serviceUri)
    {
        ArgumentNullException.ThrowIfNull(serviceUri);
        CreateClient = ct => Task.FromResult(new TableServiceClient(serviceUri, ClientOptions));
    }

    /// <summary>
    /// 使用提供的回调配置 <see cref="TableServiceClient"/>。
    /// </summary>
    public void ConfigureTableServiceClient(Func<CancellationToken, Task<TableServiceClient>> createClientCallback)
    {
        CreateClient = createClientCallback ?? throw new ArgumentNullException(nameof(createClientCallback));
    }

    /// <summary>
    /// 配置 <see cref="TableServiceClient"/>，使用经过身份验证的服务 URI 和 <see cref="TokenCredential"/>。
    /// </summary>
    public void ConfigureTableServiceClient(Uri serviceUri, TokenCredential tokenCredential)
    {
        ArgumentNullException.ThrowIfNull(serviceUri);
        ArgumentNullException.ThrowIfNull(tokenCredential);
        CreateClient = ct => Task.FromResult(new TableServiceClient(serviceUri, tokenCredential, ClientOptions));
    }

    /// <summary>
    /// 配置 <see cref="TableServiceClient"/>，使用经过身份验证的服务 URI 和 <see cref="AzureSasCredential"/>。
    /// </summary>
    public void ConfigureTableServiceClient(Uri serviceUri, AzureSasCredential azureSasCredential)
    {
        ArgumentNullException.ThrowIfNull(serviceUri);
        ArgumentNullException.ThrowIfNull(azureSasCredential);
        CreateClient = ct => Task.FromResult(new TableServiceClient(serviceUri, azureSasCredential, ClientOptions));
    }

    /// <summary>
    /// 配置 <see cref="TableServiceClient"/>，使用经过身份验证的服务 URI 和 <see cref="TableSharedKeyCredential"/>。
    /// </summary>
    public void ConfigureTableServiceClient(Uri serviceUri, TableSharedKeyCredential sharedKeyCredential)
    {
        ArgumentNullException.ThrowIfNull(serviceUri);
        ArgumentNullException.ThrowIfNull(sharedKeyCredential);
        CreateClient = ct => Task.FromResult(new TableServiceClient(serviceUri, sharedKeyCredential, ClientOptions));
    }
}
