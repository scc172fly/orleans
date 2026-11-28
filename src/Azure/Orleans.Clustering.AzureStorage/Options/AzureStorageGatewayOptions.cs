namespace Orleans.Clustering.AzureStorage;

/// <summary>
/// 配置客户端发现网关时对 Azure Table Storage 的访问。
/// </summary>
public class AzureStorageGatewayOptions : AzureStorageOperationOptions
{
    /// <inheritdoc />
    public override string TableName { get; set; } = AzureStorageClusteringOptions.DEFAULT_TABLE_NAME;
}

/// <summary>
/// <see cref="AzureStorageGatewayOptions"/> 的配置验证器。
/// </summary>
public class AzureStorageGatewayOptionsValidator : AzureStorageOperationOptionsValidator<AzureStorageGatewayOptions>
{
    /// <summary>
    /// 初始化 <see cref="AzureStorageGatewayOptionsValidator"/> 类的新实例。
    /// </summary>
    /// <param name="options">要验证的选项。</param>
    /// <param name="name">要验证的选项名称。</param>
    public AzureStorageGatewayOptionsValidator(AzureStorageGatewayOptions options, string name) : base(options, name)
    {
    }
}
