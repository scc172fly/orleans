using Orleans.Transactions.AzureStorage;

namespace Orleans.Configuration
{
    /// <summary>
    /// Azure 表格存储交易状态储存的配置选项。
    /// </summary>
    public class AzureTableTransactionalStateOptions : AzureStorageOperationOptions
    {
        /// <summary>
        /// 获取或设置存储交易状态的表格名称。
        /// </summary>
        public override string TableName { get; set; } = "TransactionalState";

        /// <summary>
        /// 获取或设定存储提供者初始化的 silo 生命周期阶段。
        /// </summary>
        public int InitStage { get; set; } = DEFAULT_INIT_STAGE;

        /// <summary>
        /// 存储提供者初始化的默认 silo 生命周期阶段。
        /// </summary>
        public const int DEFAULT_INIT_STAGE = ServiceLifecycleStage.ApplicationServices;
    }

    /// <summary>
    /// <see cref="AzureTableTransactionalStateOptions"/>的配置验证符 。
    /// </summary>
    public class AzureTableTransactionalStateOptionsValidator : AzureStorageOperationOptionsValidator<AzureTableTransactionalStateOptions>
    {
        /// <summary>
        /// 初始化 <see cref="AzureTableTransactionalStateOptionsValidator"/> 类的新实例。
        /// </summary>
        /// <param name="options">选项将生效。</param>
        /// <param name="name">需要验证的选项名称。</param>
        public AzureTableTransactionalStateOptionsValidator(AzureTableTransactionalStateOptions options, string name) : base(options, name)
        {
        }
    }
}
