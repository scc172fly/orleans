using Orleans.Core;

namespace Orleans.Persistence.Cosmos;

/// <summary>
/// Azure 宇宙DB grain 持久性的备选办法。
/// </summary>
public class CosmosGrainStorageOptions : CosmosOptions
{
    private const string ORLEANS_STORAGE_CONTAINER = "OrleansStorage";

    /// <summary>
    /// 存储提供者初始化的默认 silo 生命周期阶段。
    /// </summary>
    public const int DEFAULT_INIT_STAGE = ServiceLifecycleStage.ApplicationServices;
    private const string DEFAULT_PARTITION_KEY_PATH = "/PartitionKey";

    /// <summary>
    /// 储存应初始化的 silo 生命周期阶段,储存必须在使用前初始化。
    /// </summary>
    public int InitStage { get; set; } = DEFAULT_INIT_STAGE;

    /// <summary>
    /// 获取或设置值,以显示在调用 <see cref="IStorage.ClearStateAsync()"/> 时是否应该删除状态。
    /// </summary>
    public bool DeleteStateOnClear { get; set; }

    /// <summary>
    /// JSON 路径字符串列表。
    /// 本清单中的每一条目代表国家目标中的财产,将列入文件索引。
    /// 默认是不会在国家对象中添加任何属性。
    /// </summary>
    public List<string> StateFieldsToIndex { get; set; } = new();

    /// <summary>
    /// 获取或设置宇宙 DB 容器的分区密钥路径。
    /// </summary>
    public string PartitionKeyPath { get; set; } = DEFAULT_PARTITION_KEY_PATH;

    /// <summary>
    /// 初始化一个新的 <see cref="CosmosGrainStorageOptions"/> 实例。
    /// </summary>
    public CosmosGrainStorageOptions()
    {
        ContainerName = ORLEANS_STORAGE_CONTAINER;
    }
}
