
namespace Orleans.Transactions.Abstractions
{
    /// <summary>
    /// 配置事务状态组件（facet）。
    /// </summary>
    public interface ITransactionalStateConfiguration
    {
        /// <summary>
        /// 获取事务状态的名称。
        /// </summary>
        string StateName { get; }

        /// <summary>
        /// 获取事务状态所用的存储提供程序名称。
        /// </summary>
        string? StorageName { get; }
    }
}
