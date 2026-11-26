using System;
using Orleans.Runtime;

namespace Orleans.Configuration
{
    /// <summary>
    /// 配置 DynamoDB- 支持的流队列检查。
    /// </summary>
    public sealed class DynamoDBStreamQueueCheckpointerOptions
    {
        /// <summary>
        /// 获取或设置 AWS 区域名称或 DynamoDB 服务终点。
        /// </summary>
        public string Service { get; set; } = "us-east-1";

        /// <summary>
        /// 获取或设置 AWS 访问密钥。如果忽略,则使用默认的 AWS 证书链。
        /// </summary>
        [Redact]
        public string? AccessKey { get; set; }

        /// <summary>
        /// 获取或设置 AWS 秘密密钥 。
        /// </summary>
        [Redact]
        public string? SecretKey { get; set; }

        /// <summary>
        /// 获取或设置 AWS 会话符号 。
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
        /// 获取或设置一个值,以显示在没有检查站时是否创建检查表。
        /// </summary>
        public bool CreateIfNotExists { get; set; } = true;

        /// <summary>
        /// 获取或设定一个值,以显示表格是否使用提供输送量而不是按需计费。
        /// </summary>
        public bool UseProvisionedThroughput { get; set; }

        /// <summary>
        /// 设置或设置提供读能力单位。
        /// </summary>
        public int ReadCapacityUnits { get; set; } = 10;

        /// <summary>
        /// 获取或设置提供的书面能力单位。
        /// </summary>
        public int WriteCapacityUnits { get; set; } = 5;

        /// <summary>
        /// 获取或设定检查站之间的最小间隔。
        /// </summary>
        public TimeSpan PersistInterval { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// 获取或设定等待检查站表格启用的最大时间。
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
