
namespace Orleans.Transactions.Abstractions
{
    /// <summary>
    /// 配置交易承诺方的面板。
    /// </summary>
    public interface ITransactionCommitterConfiguration
    {
        /// <summary>
        /// 获取接受已承诺作业的服务的名称。
        /// </summary>
        string ServiceName { get; }

        /// <summary>
        /// 获取交易承诺方使用的存储提供者的名称。
        /// </summary>
        string? StorageName { get; }
    }
}
