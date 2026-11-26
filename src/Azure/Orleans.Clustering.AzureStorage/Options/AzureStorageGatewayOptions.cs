namespace Orleans.Clustering.AzureStorage;

/// <summary>
/// 配置 Azure 表存储访问,用于客户端网关发现。
/// </summary>
public class AzureStorageGatewayOptions : AzureStorageOperationOptions
{
    /// <inheritdoc />
    public override string TableName { get; set; } = AzureStorageClusteringOptions.DEFAULT_TABLE_NAME;
}

/// <summary>
/// <see cref="AzureStorageGatewayOptions"/>的配置验证符 。
/// </summary>
public class AzureStorageGatewayOptionsValidator : AzureStorageOperationOptionsValidator<AzureStorageGatewayOptions>
{
    /// <summary>
    /// 初始化 <see cref="AzureStorageGatewayOptionsValidator"/> 类的新实例。
    /// </summary>
    /// <param name="options">选项将生效。</param>
    /// <param name="name">需要验证的选项名称。</param>
    public AzureStorageGatewayOptionsValidator(AzureStorageGatewayOptions options, string name) : base(options, name)
    {
    }
}
