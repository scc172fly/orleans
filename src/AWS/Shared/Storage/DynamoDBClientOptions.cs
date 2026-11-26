#if CLUSTERING_DYNAMODB
namespace Orleans.Clustering.DynamoDB
#elif PERSISTENCE_DYNAMODB
namespace Orleans.Persistence.DynamoDB
#elif REMINDERS_DYNAMODB
namespace Orleans.Reminders.DynamoDB
#elif TRANSACTIONS_DYNAMODB
namespace Orleans.Transactions.DynamoDB
#else
// 如果某事物没有定义, 没有默认的命名空间, 故意引起编辑错误
#endif
{
    /// <summary>
    /// 配置 DynamoDB 客户端的证书和服务位置。
    /// </summary>
    public class DynamoDBClientOptions
    {
        /// <summary>
        /// AccessKey 用于 DynamoDB 储存
        /// </summary>
        [Redact]
        public string? AccessKey { get; set; }

        /// <summary>
        /// DynamoDB 存储的密密钥
        /// </summary>
        [Redact]
        public string? SecretKey { get; set; }

        /// <summary>
        /// DynamoDB 区域名称,例如“西- 西-2”
        /// </summary>
        public string Service { get; set; } = null!;

        /// <summary>
        /// DynamoDB 存储当当当当当
        /// </summary>
        [Redact]
        public string? Token { get; set; }

        /// <summary>
        /// AWS 配置文件名称 。
        /// </summary>
        public string? ProfileName { get; set; }
    }
}
