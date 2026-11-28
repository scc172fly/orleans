namespace Orleans.Clustering.Cosmos;

/// <summary>
/// 用于配置 Azure Cosmos DB 集群的选项。
/// </summary>
/// <remarks>
/// 成员资格管理继承客户端的读取一致性，并使用带表版本屏障的单分区事务。
/// 将账户配置为单个可写区域和强一致性，同时将客户端配置为使用
/// 强一致性或继承账户默认一致性。
/// </remarks>
public class CosmosClusteringOptions : CosmosOptions
{
    private const string ORLEANS_CLUSTER_CONTAINER = "OrleansCluster";

    /// <summary>
    /// 初始化新的 <see cref="CosmosClusteringOptions"/> 实例。
    /// </summary>
    public CosmosClusteringOptions()
    {
        ContainerName = ORLEANS_CLUSTER_CONTAINER;
    }
}

/// <summary>
/// <see cref="CosmosClusteringOptions"/> 的配置验证器。
/// </summary>
public class CosmosClusteringOptionsValidator : CosmosOptionsValidator<CosmosClusteringOptions>
{
    /// <summary>
    /// 初始化 <see cref="CosmosClusteringOptionsValidator"/> 类的新实例。
    /// </summary>
    /// <param name="options">要验证的选项。</param>
    /// <param name="name">要验证的选项名称。</param>
    public CosmosClusteringOptionsValidator(CosmosClusteringOptions options, string name) : base(options, name)
    {
    }
}
