using Orleans.Persistence.AzureStorage;
using Orleans.Storage;

namespace Orleans.Configuration
{
    /// <summary>
    /// AzureTableGrainStorage 的配置。
    /// </summary>
    public class AzureTableStorageOptions : AzureStorageOperationOptions, IStorageProviderSerializerOptions
    {
        /// <summary>
        /// 存储 Grain 状态的表名。
        /// </summary>
        public override string TableName { get; set; } = DEFAULT_TABLE_NAME;

        /// <summary>
        /// 用于存储 Grain 状态的默认 Azure Table 表名。
        /// </summary>
        public const string DEFAULT_TABLE_NAME = "OrleansGrainState";

        /// <summary>
        /// 指示 Grain 清除其状态时，应删除 Grain 数据还是将其重置为默认值。
        /// </summary>
        public bool DeleteStateOnClear { get; set; } = false;

        /// <summary>
        /// 指示 Grain 数据应以字符串还是二进制格式存储。
        /// </summary>
        public bool UseStringFormat { get; set; }

        /// <summary>
        /// 应初始化存储的 Silo 生命周期阶段。存储必须在使用前完成初始化。
        /// </summary>
        public int InitStage { get; set; } = DEFAULT_INIT_STAGE;

        /// <summary>
        /// 初始化存储提供程序的默认 Silo 生命周期阶段。
        /// </summary>
        public const int DEFAULT_INIT_STAGE = ServiceLifecycleStage.ApplicationServices;

        /// <inheritdoc/>
        public IGrainStorageSerializer GrainStorageSerializer { get; set; } = null!;
    }

    /// <summary>
    /// AzureTableStorageOptions 的配置验证器。
    /// </summary>
    public class AzureTableGrainStorageOptionsValidator : AzureStorageOperationOptionsValidator<AzureTableStorageOptions>
    {
        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="options">要验证的选项。</param>
        /// <param name="name">要验证的选项名称。</param>
        public AzureTableGrainStorageOptionsValidator(AzureTableStorageOptions options, string name) : base(options, name)
        {
        }
    }
}
