namespace Orleans.Transactions.DynamoDB.TransactionalState;

/// <summary>
/// DynamoDB 事务状态存储使用的常量。
/// </summary>
public static class DynamoDBTransactionalStateConstants
{
    /// <summary>
    /// DynamoDB 分区键的属性名称。
    /// </summary>
    public const string PARTITION_KEY_PROPERTY_NAME = "PartitionKey";

    /// <summary>
    /// DynamoDB 行键的属性名称。
    /// </summary>
    public const string ROW_KEY_PROPERTY_NAME = "RowKey";

    /// <summary>
    /// 已序列化 Grain 状态的属性名称。
    /// </summary>
    public const string BINARY_STATE_PROPERTY_NAME = "GrainState";

    /// <summary>
    /// 实体标记的属性名称。
    /// </summary>
    public const string ETAG_PROPERTY_NAME = "ETag";

    /// <summary>
    /// 时间戳属性名称。
    /// </summary>
    public const string TIMESTAMP_PROPERTY_NAME = "Timestamp";

    /// <summary>
    /// 当前实体标记的表达式别名。
    /// </summary>
    public const string CURRENT_ETAG_ALIAS = ":currentETag";
}
