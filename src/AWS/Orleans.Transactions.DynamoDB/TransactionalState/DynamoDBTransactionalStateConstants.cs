namespace Orleans.Transactions.DynamoDB.TransactionalState;

/// <summary>
/// DynamoDB 交易状态储存所使用的常数。
/// </summary>
public static class DynamoDBTransactionalStateConstants
{
    /// <summary>
    /// DynamoDB 分区密钥属性名称。
    /// </summary>
    public const string PARTITION_KEY_PROPERTY_NAME = "PartitionKey";

    /// <summary>
    /// 行 DynamoDB 密钥属性名称。
    /// </summary>
    public const string ROW_KEY_PROPERTY_NAME = "RowKey";

    /// <summary>
    /// 序列化的 grain 状态属性名称。
    /// </summary>
    public const string BINARY_STATE_PROPERTY_NAME = "GrainState";

    /// <summary>
    /// 实体标签属性名称 。
    /// </summary>
    public const string ETAG_PROPERTY_NAME = "ETag";

    /// <summary>
    /// 时间戳属性名称 。
    /// </summary>
    public const string TIMESTAMP_PROPERTY_NAME = "Timestamp";

    /// <summary>
    /// 当前实体标签的表达式别名 。
    /// </summary>
    public const string CURRENT_ETAG_ALIAS = ":currentETag";
}
