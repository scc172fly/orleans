using System;
using Orleans.Runtime;

namespace Orleans.Configuration
{
    /// <summary>
    /// 配置由 DynamoDB 支持的流队列检查点记录。
    /// </summary>
    public sealed class DynamoDBStreamQueueCheckpointerOptions
    {
        /// <summary>
        /// 获取或设置 AWS 区域名称或 DynamoDB 服务终结点。
        /// </summary>
        public string Service { get; set; } = "us-east-1";

        /// <summary>
        /// 获取或设置 AWS 访问密钥 ID。省略时使用默认 AWS 凭据链。
        /// </summary>
        [Redact]
        public string? AccessKey { get; set; }

        /// <summary>
        /// 获取或设置 AWS 私有访问密钥。
        /// </summary>
        [Redact]
        public string? SecretKey { get; set; }

        /// <summary>
        /// 获取或设置 AWS 会话令牌。
        /// </summary>
        [Redact]
        public string? Token { get; set; }

        /// <summary>
        /// 获取或设置 AWS 配置文件名称。
        /// </summary>
        public string? ProfileName { get; set; }

        /// <summary>
        /// 获取或设置 DynamoDB 表名称。
        /// </summary>
        public string TableName { get; set; } = "OrleansStreamCheckpoints";

        /// <summary>
        /// 获取或设置一个值，指示检查点表不存在时是否创建它。
        /// </summary>
        public bool CreateIfNotExists { get; set; } = true;

        /// <summary>
        /// 获取或设置一个值，指示表是否使用预置吞吐量而非按需计费。
        /// </summary>
        public bool UseProvisionedThroughput { get; set; }

        /// <summary>
        /// 获取或设置预置读取容量单位数。
        /// </summary>
        public int ReadCapacityUnits { get; set; } = 10;

        /// <summary>
        /// 获取或设置预置写入容量单位数。
        /// </summary>
        public int WriteCapacityUnits { get; set; } = 5;

        /// <summary>
        /// 获取或设置检查点写入之间的最短间隔。
        /// </summary>
        public TimeSpan PersistInterval { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// 获取或设置等待检查点表进入活动状态的最长时间。
        /// </summary>
        public TimeSpan InitializationTimeout { get; set; } = TimeSpan.FromMinutes(2);
    }

    internal sealed class DynamoDBStreamQueueCheckpointerOptionsValidator(
        DynamoDBStreamQueueCheckpointerOptions options,
        string name) : IConfigurationValidator
    {
        public void ValidateConfiguration()
        {
            if (string.IsNullOrWhiteSpace(options.Service))
            {
                throw new OrleansConfigurationException(
                    $"The {nameof(DynamoDBStreamQueueCheckpointerOptions.Service)} is required for the DynamoDB checkpointer '{name}'.");
            }

            if (string.IsNullOrWhiteSpace(options.TableName))
            {
                throw new OrleansConfigurationException(
                    $"The {nameof(DynamoDBStreamQueueCheckpointerOptions.TableName)} is required for the DynamoDB checkpointer '{name}'.");
            }

            if (options.PersistInterval <= TimeSpan.Zero)
            {
                throw new OrleansConfigurationException(
                    $"The {nameof(DynamoDBStreamQueueCheckpointerOptions.PersistInterval)} must be greater than zero for the DynamoDB checkpointer '{name}'.");
            }

            if (options.InitializationTimeout <= TimeSpan.Zero)
            {
                throw new OrleansConfigurationException(
                    $"The {nameof(DynamoDBStreamQueueCheckpointerOptions.InitializationTimeout)} must be greater than zero for the DynamoDB checkpointer '{name}'.");
            }

            if (options.UseProvisionedThroughput
                && (options.ReadCapacityUnits <= 0 || options.WriteCapacityUnits <= 0))
            {
                throw new OrleansConfigurationException(
                    $"The {nameof(DynamoDBStreamQueueCheckpointerOptions.ReadCapacityUnits)} and " +
                    $"{nameof(DynamoDBStreamQueueCheckpointerOptions.WriteCapacityUnits)} must be greater than zero " +
                    $"when provisioned throughput is enabled for the DynamoDB checkpointer '{name}'.");
            }

            var hasAccessKey = !string.IsNullOrEmpty(options.AccessKey);
            var hasSecretKey = !string.IsNullOrEmpty(options.SecretKey);
            if (hasAccessKey != hasSecretKey)
            {
                throw new OrleansConfigurationException(
                    $"The {nameof(DynamoDBStreamQueueCheckpointerOptions.AccessKey)} and " +
                    $"{nameof(DynamoDBStreamQueueCheckpointerOptions.SecretKey)} must either both be configured or both be omitted " +
                    $"for the DynamoDB checkpointer '{name}'.");
            }

            if (!string.IsNullOrEmpty(options.Token) && !hasAccessKey)
            {
                throw new OrleansConfigurationException(
                    $"The {nameof(DynamoDBStreamQueueCheckpointerOptions.Token)} requires explicit credentials " +
                    $"for the DynamoDB checkpointer '{name}'.");
            }

            if (hasAccessKey && !string.IsNullOrEmpty(options.ProfileName))
            {
                throw new OrleansConfigurationException(
                    $"Explicit credentials and {nameof(DynamoDBStreamQueueCheckpointerOptions.ProfileName)} cannot both be configured " +
                    $"for the DynamoDB checkpointer '{name}'.");
            }
        }
    }
}
