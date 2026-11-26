using System;
using Orleans.Persistence.DynamoDB;
using Orleans.Runtime;
using Orleans.Storage;

namespace Orleans.Configuration
{
    /// <summary>
    /// 配置 DynamoDB grain 存储提供者。
    /// </summary>
    public class DynamoDBStorageOptions : DynamoDBClientOptions, IStorageProviderSerializerOptions
    {
        /// <summary>
        /// 获取或设置此项服务的独特识别符,该服务应能在部署和重新部署后生存。
        /// </summary>
        /// <remarks>
        /// 值前缀每个 grain 状态的键,必须明确设置。
        /// 对于新的部署,使用稳定识别符号,如 <see cref="ClusterOptions.ServiceId"/>。
        /// 对于现有的部署,保留用于写出其状态的数值,包括
        /// 从上一个空字符串默认值升级时<see cref="string.Empty"/> 。
        /// </remarks>
        public string? ServiceId { get; set; }

        /// <summary>
        /// 表中已提供投入的用途
        /// </summary>
        public bool UseProvisionedThroughput { get; set; } = true;

        /// <summary>
        /// 如果表格不存在创建表格
        /// </summary>
        public bool CreateIfNotExists { get; set; } = true;

        /// <summary>
        /// 如果有更新表格,则更新列表
        /// </summary>
        public bool UpdateIfExists { get; set; } = true;

        /// <summary>
        /// DynamoDB 储存读能力单位
        /// </summary>
        public int ReadCapacityUnits { get; set; } = DynamoDBStorage.DefaultReadCapacityUnits;

        /// <summary>
        /// DynamoDB 储存的写能力单位
        /// </summary>
        public int WriteCapacityUnits { get; set; } = DynamoDBStorage.DefaultWriteCapacityUnits;

        /// <summary>
        /// DynamoDB 表格名称。
        /// 默认值为 'OrleansGrainState' 。
        /// </summary>
        public string TableName { get; set; } = "OrleansGrainState";

        /// <summary>
        /// 表明 grain 数据应删除,或当 grain 清清楚楚,这是状态。
        /// </summary>
        public bool DeleteStateOnClear { get; set; } = false;

        /// <summary>
        /// 储存应初始化的 silo 生命周期阶段,储存必须在使用前初始化。
        /// </summary>
        public int InitStage { get; set; } = DEFAULT_INIT_STAGE;

        /// <summary>
        /// 存储提供者初始化的默认 silo 生命周期阶段。
        /// </summary>
        public const int DEFAULT_INIT_STAGE = ServiceLifecycleStage.ApplicationServices;

        /// <summary>
        /// 指定项目未来过期的期间
        /// 每 StateWrite 将增加 grain的TTL
        /// </summary>
        public TimeSpan? TimeToLive { get; set; }

        /// <inheritdoc/>
        public IGrainStorageSerializer GrainStorageSerializer { get; set; } = null!;
    }

    /// <summary>
    /// DynamoDBStorageOptions的配置验证符
    /// </summary>
    public class DynamoDBGrainStorageOptionsValidator : IConfigurationValidator
    {
        private readonly DynamoDBStorageOptions options;
        private readonly string name;

        /// <summary>
        /// 构造建构器
        /// </summary>
        /// <param name="options">选项将生效。</param>
        /// <param name="name">需要验证的选项名称。</param>
        public DynamoDBGrainStorageOptionsValidator(DynamoDBStorageOptions options, string name)
        {
            this.options = options;
            this.name = name;
        }

        /// <inheritdoc/>
        public void ValidateConfiguration()
        {
            if (this.options.ServiceId is null)
                throw new OrleansConfigurationException(
                    $"Configuration for DynamoDBGrainStorage {this.name} is invalid. {nameof(this.options.ServiceId)} must be set explicitly. For a new deployment, use a stable identifier such as the cluster's ServiceId. For an existing deployment, retain the ServiceId used to write its state; set ServiceId to string.Empty if it previously used the default.");

            if (string.IsNullOrWhiteSpace(this.options.TableName))
                throw new OrleansConfigurationException(
                    $"Configuration for DynamoDBGrainStorage {this.name} is invalid. {nameof(this.options.TableName)} is not valid.");

            if (this.options.UseProvisionedThroughput)
            {
                if (this.options.ReadCapacityUnits == 0)
                    throw new OrleansConfigurationException(
                        $"Configuration for DynamoDBGrainStorage {this.name} is invalid. {nameof(this.options.ReadCapacityUnits)} is not valid.");

                if (this.options.WriteCapacityUnits == 0)
                    throw new OrleansConfigurationException(
                        $"Configuration for DynamoDBGrainStorage {this.name} is invalid. {nameof(this.options.WriteCapacityUnits)} is not valid.");
            }
        }
    }
}
