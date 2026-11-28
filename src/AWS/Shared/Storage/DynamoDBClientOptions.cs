#if CLUSTERING_DYNAMODB
namespace Orleans.Clustering.DynamoDB
#elif PERSISTENCE_DYNAMODB
namespace Orleans.Persistence.DynamoDB
#elif REMINDERS_DYNAMODB
namespace Orleans.Reminders.DynamoDB
#elif TRANSACTIONS_DYNAMODB
namespace Orleans.Transactions.DynamoDB
#else
// 有意不使用默认命名空间，以便未定义某项内容时产生编译错误。
#endif
{
    /// <summary>
    /// 配置 DynamoDB 客户端的凭据和服务位置。
    /// </summary>
    public class DynamoDBClientOptions
    {
        /// <summary>
        /// DynamoDB 存储的 AccessKey 字符串。
        /// </summary>
        [Redact]
        public string? AccessKey { get; set; }

        /// <summary>
        /// DynamoDB 存储的私有访问密钥。
        /// </summary>
        [Redact]
        public string? SecretKey { get; set; }

        /// <summary>
        /// DynamoDB 区域名称，例如 "us-west-2"。
        /// </summary>
        public string Service { get; set; } = null!;

        /// <summary>
        /// DynamoDB 存储的令牌。
        /// </summary>
        [Redact]
        public string? Token { get; set; }

        /// <summary>
        /// AWS 配置文件名称。
        /// </summary>
        public string? ProfileName { get; set; }
    }
}
