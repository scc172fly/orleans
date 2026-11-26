
namespace Orleans.Transactions.Abstractions
{
    /// <summary>
    /// 配置交易状态表。
    /// </summary>
    public interface ITransactionalStateConfiguration
    {
        /// <summary>
        /// 获得交易国名 。
        /// </summary>
        string StateName { get; }

        /// <summary>
        /// 获取用于交易状态的存储提供者的名称 。
        /// </summary>
        string? StorageName { get; }
    }
}
