
namespace Orleans.Transactions.Abstractions
{
    /// <summary>
    /// 配置事务提交器组件（facet）。
    /// </summary>
    public interface ITransactionCommitterConfiguration
    {
        /// <summary>
        /// 获取接收已提交操作的服务名称。
        /// </summary>
        string ServiceName { get; }

        /// <summary>
        /// 获取事务提交器使用的存储提供程序名称。
        /// </summary>
        string? StorageName { get; }
    }
}
