using System;
using Orleans.Persistence.DynamoDB;
using Orleans.Runtime;
using Orleans.Storage;

namespace Orleans.Configuration
{
    /// <summary>
    /// 配置 DynamoDB Grain 存储提供程序。
    /// </summary>
    public class DynamoDBStorageOptions : DynamoDBClientOptions, IStorageProviderSerializerOptions
    {
        /// <summary>
        /// 获取或设置此服务的唯一标识，该标识应在部署及重新部署后保持不变。
        /// </summary>
        /// <remarks>
        /// 此值作为每个 Grain 状态键的前缀，必须显式设置。
        /// 对于新部署，使用诸如 <see cref="ClusterOptions.ServiceId"/> 的稳定标识。
        /// 对于现有部署，保留原先写入状态时使用的值，包括
        /// 从此前默认空字符串的版本升级时使用的 <see cref="string.Empty"/>。
        /// </remarks>
        public string? ServiceId { get; set; }

        /// <summary>
        /// 对表使用预置吞吐量。
        /// </summary>
        public bool UseProvisionedThroughput { get; set; } = true;

        /// <summary>
        /// 如果表不存在，则创建该表。
        /// </summary>
        public bool CreateIfNotExists { get; set; } = true;

        /// <summary>
        /// 如果表存在，则更新该表。
        /// </summary>
        public bool UpdateIfExists { get; set; } = true;

        /// <summary>
        /// DynamoDB 存储的读取容量单位。
        /// </summary>
        public int ReadCapacityUnits { get; set; } = DynamoDBStorage.DefaultReadCapacityUnits;

        /// <summary>
        /// DynamoDB 存储的写入容量单位。
        /// </summary>
        public int WriteCapacityUnits { get; set; } = DynamoDBStorage.DefaultWriteCapacityUnits;

        /// <summary>
        /// DynamoDB 表名称。
        /// 默认值为 OrleansGrainState。
        /// </summary>
        public string TableName { get; set; } = "OrleansGrainState";

        /// <summary>
        /// 指示 Grain 清除状态时应删除 Grain 数据还是将其重置为默认值。
        /// </summary>
        public bool DeleteStateOnClear { get; set; } = false;

        /// <summary>
        /// 应初始化存储的 Silo 生命周期阶段。存储必须在使用前完成初始化。
        /// </summary>
        public int InitStage { get; set; } = DEFAULT_INIT_STAGE;

        /// <summary>
        /// 初始化存储提供程序的默认 Silo 生命周期阶段。
        /// </summary>
        public const int DEFAULT_INIT_STAGE = ServiceLifecycleStage.ApplicationServices;

        /// <summary>
        /// 指定条目距未来过期的时间跨度。
        /// 每次 StateWrite 都会延长 Grain 的 TTL。
        /// </summary>
        public TimeSpan? TimeToLive { get; set; }

        /// <inheritdoc/>
        public IGrainStorageSerializer GrainStorageSerializer { get; set; } = null!;
    }

    /// <summary>
    /// DynamoDBStorageOptions 的配置验证器。
    /// </summary>
    public class DynamoDBGrainStorageOptionsValidator : IConfigurationValidator
    {
        private readonly DynamoDBStorageOptions options;
        private readonly string name;

        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="options">要验证的选项。</param>
        /// <param name="name">要验证的选项名称。</param>
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
