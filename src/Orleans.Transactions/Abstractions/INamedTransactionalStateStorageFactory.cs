
namespace Orleans.Transactions.Abstractions
{
    /// <summary>
    /// 创建 ITransactionalStateStorage 名称的工厂。
    /// </summary>
    public interface INamedTransactionalStateStorageFactory
    {
        /// <summary>
        /// 创建 ITransactionalStateStorage 名称。
        /// </summary>
        /// <typeparam name="TState">交易状态类型。</typeparam>
        /// <param name="storageName">要创建的交易国存储的名称。</param>
        /// <param name="stateName">交易状态名称。</param>
        /// <returns>交易状态储存。</returns>
        /// <exception cref="System.InvalidOperationException">
        /// 没有交易性的国家储存厂或 grain 存储供应商配置为要求的名称。
        /// </exception>
        ITransactionalStateStorage<TState> Create<TState>(string? storageName, string stateName) where TState : class, new();
    }
}
