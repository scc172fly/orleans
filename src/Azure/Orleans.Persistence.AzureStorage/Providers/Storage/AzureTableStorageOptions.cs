using Orleans.Persistence.AzureStorage;
using Orleans.Storage;

namespace Orleans.Configuration
{
    /// <summary>
    /// AzureTableGrainStorage配置配置
    /// </summary>
    public class AzureTableStorageOptions : AzureStorageOperationOptions, IStorageProviderSerializerOptions
    {
        /// <summary>
        /// 存储 grain 阶段的表格名称
        /// </summary>
        public override string TableName { get; set; } = DEFAULT_TABLE_NAME;

        /// <summary>
        /// 默认默认 Azure 用于存储的表格名称 grain 状态。
        /// </summary>
        public const string DEFAULT_TABLE_NAME = "OrleansGrainState";

        /// <summary>
        /// 表明 grain 数据应删除,或当 grain 清清楚楚,这是状态。
        /// </summary>
        public bool DeleteStateOnClear { get; set; } = false;

        /// <summary>
        /// 表示 grain 数据是否应当以字符串或二进制格式存储。
        /// </summary>
        public bool UseStringFormat { get; set; }

        /// <summary>
        /// 储存应初始化的 silo 生命周期阶段,储存必须在使用前初始化。
        /// </summary>
        public int InitStage { get; set; } = DEFAULT_INIT_STAGE;

        /// <summary>
        /// 存储提供者初始化的默认 silo 生命周期阶段。
        /// </summary>
        public const int DEFAULT_INIT_STAGE = ServiceLifecycleStage.ApplicationServices;

        /// <inheritdoc/>
        public IGrainStorageSerializer GrainStorageSerializer { get; set; } = null!;
    }

    /// <summary>
    /// AzureTableStorageOptions的配置验证符
    /// </summary>
    public class AzureTableGrainStorageOptionsValidator : AzureStorageOperationOptionsValidator<AzureTableStorageOptions>
    {
        /// <summary>
        /// 构造建构器
        /// </summary>
        /// <param name="options">选项将生效。</param>
        /// <param name="name">需要验证的选项名称。</param>
        public AzureTableGrainStorageOptionsValidator(AzureTableStorageOptions options, string name) : base(options, name)
        {
        }
    }
}
