namespace Orleans.Clustering.AzureStorage;

/// <summary>
/// 指定 AzureTableBasedMembership所使用的选项
/// </summary>
public class AzureStorageClusteringOptions : AzureStorageOperationOptions
{
    /// <inheritdoc />
    public override string TableName { get; set; } = DEFAULT_TABLE_NAME;

    /// <summary>
    /// 用于分组成员资格的默认 Azure 表格存储表名称。
    /// </summary>
    public const string DEFAULT_TABLE_NAME = "OrleansSiloInstances";
}

/// <summary>
/// <see cref="AzureStorageClusteringOptions"/>的配置验证符 。
/// </summary>
public class AzureStorageClusteringOptionsValidator : AzureStorageOperationOptionsValidator<AzureStorageClusteringOptions>
{
    /// <summary>
    /// 初始化 <see cref="AzureStorageClusteringOptionsValidator"/> 类的新实例。
    /// </summary>
    /// <param name="options">选项将生效。</param>
    /// <param name="name">需要验证的选项名称。</param>
    public AzureStorageClusteringOptionsValidator(AzureStorageClusteringOptions options, string name) : base(options, name)
    {
    }
}
