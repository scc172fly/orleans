namespace Orleans.Clustering.AzureStorage;

/// <summary>
/// 指定 AzureTableBasedMembership 使用的选项。
/// </summary>
public class AzureStorageClusteringOptions : AzureStorageOperationOptions
{
    /// <inheritdoc />
    public override string TableName { get; set; } = DEFAULT_TABLE_NAME;

    /// <summary>
    /// 用于集群成员资格的默认 Azure Table Storage 表名。
    /// </summary>
    public const string DEFAULT_TABLE_NAME = "OrleansSiloInstances";
}

/// <summary>
/// <see cref="AzureStorageClusteringOptions"/> 的配置验证器。
/// </summary>
public class AzureStorageClusteringOptionsValidator : AzureStorageOperationOptionsValidator<AzureStorageClusteringOptions>
{
    /// <summary>
    /// 初始化 <see cref="AzureStorageClusteringOptionsValidator"/> 类的新实例。
    /// </summary>
    /// <param name="options">要验证的选项。</param>
    /// <param name="name">要验证的选项名称。</param>
    public AzureStorageClusteringOptionsValidator(AzureStorageClusteringOptions options, string name) : base(options, name)
    {
    }
}
