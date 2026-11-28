using Orleans.GrainDirectory.AzureStorage;

namespace Orleans.Configuration
{
    /// <summary>
    /// 配置 Azure Table Storage Grain 目录。
    /// </summary>
    public class AzureTableGrainDirectoryOptions : AzureStorageOperationOptions
    {
        /// <summary>
        /// 获取或设置 Azure Table Storage 表名。
        /// </summary>
        public override string TableName { get; set; } = DEFAULT_TABLE_NAME;

        /// <summary>
        /// 默认 Azure Table Storage 表名。
        /// </summary>
        public const string DEFAULT_TABLE_NAME = "GrainDirectory";
    }

    /// <summary>
    /// 验证 <see cref="AzureTableGrainDirectoryOptions"/>。
    /// </summary>
    public class AzureTableGrainDirectoryOptionsValidator : AzureStorageOperationOptionsValidator<AzureTableGrainDirectoryOptions>
    {
        /// <summary>
        /// 初始化 <see cref="AzureTableGrainDirectoryOptionsValidator"/> 类的新实例。
        /// </summary>
        /// <param name="options">要验证的选项。</param>
        /// <param name="name">Grain 目录名称。</param>
        public AzureTableGrainDirectoryOptionsValidator(AzureTableGrainDirectoryOptions options, string name) : base(options, name)
        {
        }
    }
}
