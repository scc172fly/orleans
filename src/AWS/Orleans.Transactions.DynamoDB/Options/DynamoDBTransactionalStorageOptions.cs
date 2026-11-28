using Orleans.Runtime;
using Orleans.Storage;
#if CLUSTERING_DYNAMODB
using Orleans.Clustering.DynamoDB;
#elif PERSISTENCE_DYNAMODB
using Orleans.Persistence.DynamoDB;
#elif REMINDERS_DYNAMODB
using Orleans.Reminders.DynamoDB;
#elif AWSUTILS_TESTS
using Orleans.AWSUtils.Tests;
#elif TRANSACTIONS_DYNAMODB
using Orleans.Transactions.DynamoDB;
#else
#endif

namespace Orleans.Configuration;

/// <summary>
/// DynamoDB 事务状态存储的配置选项。
/// </summary>
public class DynamoDBTransactionalStorageOptions : DynamoDBClientOptions, IStorageProviderSerializerOptions
{
    /// <summary>
    /// 获取或设置此服务的唯一标识，该标识应在部署及重新部署后保持不变。
    /// </summary>
    public string ServiceId { get; set; } = string.Empty;

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
    /// 默认值为 OrleansTransactionalState。
    /// </summary>
    public string TableName { get; set; } = "OrleansTransactionalState";

    /// <summary>
    /// 应初始化存储的 Silo 生命周期阶段。存储必须在使用前完成初始化。
    /// </summary>
    public int InitStage { get; set; } = DEFAULT_INIT_STAGE;

    /// <summary>
    /// 初始化事务状态存储的默认 Silo 生命周期阶段。
    /// </summary>
    public const int DEFAULT_INIT_STAGE = ServiceLifecycleStage.ApplicationServices;

    /// <summary>
    /// 获取或设置用于序列化 Grain 状态的序列化器。
    /// </summary>
    public IGrainStorageSerializer GrainStorageSerializer { get; set; } = null!;
}

/// <summary>
/// DynamoDBTransactionalStorageOptions 的配置验证器。
/// </summary>
public class DynamoDBTransactionalStorageOptionsValidator : IConfigurationValidator
{
    private readonly DynamoDBTransactionalStorageOptions options;
    private readonly string name;

    /// <summary>
    /// 构造函数。
    /// </summary>
    /// <param name="options">要验证的选项。</param>
    /// <param name="name">要验证的选项名称。</param>
    public DynamoDBTransactionalStorageOptionsValidator(DynamoDBTransactionalStorageOptions options, string name)
    {
        this.options = options;
        this.name = name;
    }

    /// <inheritdoc />
    public void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(this.options.TableName))
            throw new OrleansConfigurationException(
                $"Configuration for DynamoDBTransactionalStateStorage {this.name} is invalid. {nameof(this.options.TableName)} is not valid.");

        if (this.options.UseProvisionedThroughput)
        {
            if (this.options.ReadCapacityUnits == 0)
                throw new OrleansConfigurationException(
                    $"Configuration for DynamoDBTransactionalStateStorage {this.name} is invalid. {nameof(this.options.ReadCapacityUnits)} is not valid.");

            if (this.options.WriteCapacityUnits == 0)
                throw new OrleansConfigurationException(
                    $"Configuration for DynamoDBTransactionalStateStorage {this.name} is invalid. {nameof(this.options.WriteCapacityUnits)} is not valid.");
        }
    }
}
