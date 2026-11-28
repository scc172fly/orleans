using Orleans.Transactions.AzureStorage;

namespace Orleans.Configuration
{
    /// <summary>
    /// Azure Table Storage 事务状态存储的配置选项。
    /// </summary>
    public class AzureTableTransactionalStateOptions : AzureStorageOperationOptions
    {
        /// <summary>
        /// 获取或设置存储事务状态的表名。
        /// </summary>
        public override string TableName { get; set; } = "TransactionalState";

        /// <summary>
        /// 获取或设置初始化存储提供程序的 Silo 生命周期阶段。
        /// </summary>
        public int InitStage { get; set; } = DEFAULT_INIT_STAGE;

        /// <summary>
        /// 初始化存储提供程序的默认 Silo 生命周期阶段。
        /// </summary>
        public const int DEFAULT_INIT_STAGE = ServiceLifecycleStage.ApplicationServices;
    }

    /// <summary>
    /// <see cref="AzureTableTransactionalStateOptions"/> 的配置验证器。
    /// </summary>
    public class AzureTableTransactionalStateOptionsValidator : AzureStorageOperationOptionsValidator<AzureTableTransactionalStateOptions>
    {
        /// <summary>
        /// 初始化 <see cref="AzureTableTransactionalStateOptionsValidator"/> 类的新实例。
        /// </summary>
        /// <param name="options">要验证的选项。</param>
        /// <param name="name">要验证的选项名称。</param>
        public AzureTableTransactionalStateOptionsValidator(AzureTableTransactionalStateOptions options, string name) : base(options, name)
        {
        }
    }
}
