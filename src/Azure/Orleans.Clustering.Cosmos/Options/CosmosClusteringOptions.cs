namespace Orleans.Clustering.Cosmos;

/// <summary>
/// 配置 Azure 宇宙 DB 组合的选项。
/// </summary>
/// <remarks>
/// 会员资格继承客户阅读的一致性,使用单部分交易和台词栏。
/// 配置账户,配置时设置单一可写区域、强一致性和客户端配置
/// 保持强有力的一致性或继承账户违约。
/// </remarks>
public class CosmosClusteringOptions : CosmosOptions
{
    private const string ORLEANS_CLUSTER_CONTAINER = "OrleansCluster";

    /// <summary>
    /// 初始化一个新的 <see cref="CosmosClusteringOptions"/> 实例。
    /// </summary>
    public CosmosClusteringOptions()
    {
        ContainerName = ORLEANS_CLUSTER_CONTAINER;
    }
}

/// <summary>
/// <see cref="CosmosClusteringOptions"/>的配置验证符 。
/// </summary>
public class CosmosClusteringOptionsValidator : CosmosOptionsValidator<CosmosClusteringOptions>
{
    /// <summary>
    /// 初始化 <see cref="CosmosClusteringOptionsValidator"/> 类的新实例。
    /// </summary>
    /// <param name="options">选项将生效。</param>
    /// <param name="name">需要验证的选项名称。</param>
    public CosmosClusteringOptionsValidator(CosmosClusteringOptions options, string name) : base(options, name)
    {
    }
}
