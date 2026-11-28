using Orleans.Core;

namespace Orleans.Persistence.Cosmos;

/// <summary>
/// Azure Cosmos DB Grain 持久化选项。
/// </summary>
public class CosmosGrainStorageOptions : CosmosOptions
{
    private const string ORLEANS_STORAGE_CONTAINER = "OrleansStorage";

    /// <summary>
    /// 初始化存储提供程序的默认 Silo 生命周期阶段。
    /// </summary>
    public const int DEFAULT_INIT_STAGE = ServiceLifecycleStage.ApplicationServices;
    private const string DEFAULT_PARTITION_KEY_PATH = "/PartitionKey";

    /// <summary>
    /// 初始化存储的 Silo 生命周期阶段。存储必须在使用前完成初始化。
    /// </summary>
    public int InitStage { get; set; } = DEFAULT_INIT_STAGE;

    /// <summary>
    /// 获取或设置一个值，指示调用 <see cref="IStorage.ClearStateAsync()"/> 时是否应删除状态。
    /// </summary>
    public bool DeleteStateOnClear { get; set; }

    /// <summary>
    /// JSON 路径字符串列表。
    /// 列表中的每一项表示状态对象中应包含在文档索引里的属性。
    /// 默认不添加状态对象中的任何属性。
    /// </summary>
    public List<string> StateFieldsToIndex { get; set; } = new();

    /// <summary>
    /// 获取或设置 Cosmos DB 容器的分区键路径。
    /// </summary>
    public string PartitionKeyPath { get; set; } = DEFAULT_PARTITION_KEY_PATH;

    /// <summary>
    /// 初始化新的 <see cref="CosmosGrainStorageOptions"/> 实例。
    /// </summary>
    public CosmosGrainStorageOptions()
    {
        ContainerName = ORLEANS_STORAGE_CONTAINER;
    }
}
