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
/// DynamoDB 交易状态存储的配置选项。
/// </summary>
public class DynamoDBTransactionalStorageOptions : DynamoDBClientOptions, IStorageProviderSerializerOptions
{
    /// <summary>
    /// 获取或设置此项服务的独特识别符,该服务应能在部署和重新部署后生存。
    /// </summary>
    public string ServiceId { get; set; } = string.Empty;

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
    /// 默认值为 'OrleansTransactionalState' 。
    /// </summary>
    public string TableName { get; set; } = "OrleansTransactionalState";

    /// <summary>
    /// 储存应初始化的 silo 生命周期阶段,储存必须在使用前初始化。
    /// </summary>
    public int InitStage { get; set; } = DEFAULT_INIT_STAGE;

    /// <summary>
    /// 启动交易状态储存的默认 silo 生命周期阶段。
    /// </summary>
    public const int DEFAULT_INIT_STAGE = ServiceLifecycleStage.ApplicationServices;

    /// <summary>
    /// 获取或设置序列化 grain 状态的序列化器。
    /// </summary>
    public IGrainStorageSerializer GrainStorageSerializer { get; set; } = null!;
}

/// <summary>
/// DynamoDBTransactionalStorageOptions的配置验证符
/// </summary>
public class DynamoDBTransactionalStorageOptionsValidator : IConfigurationValidator
{
    private readonly DynamoDBTransactionalStorageOptions options;
    private readonly string name;

    /// <summary>
    /// 构造建构器
    /// </summary>
    /// <param name="options">选项将生效。</param>
    /// <param name="name">需要验证的选项名称。</param>
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
