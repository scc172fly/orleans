
namespace Orleans.Transactions.Abstractions
{
    /// <summary>
    /// 根据名称创建 ITransactionalStateStorage 的工厂。
    /// </summary>
    public interface INamedTransactionalStateStorageFactory
    {
        /// <summary>
        /// 根据名称创建 ITransactionalStateStorage。
        /// </summary>
        /// <typeparam name="TState">事务状态类型。</typeparam>
        /// <param name="storageName">要创建的事务状态存储的名称。</param>
        /// <param name="stateName">事务状态的名称。</param>
        /// <returns>事务状态存储。</returns>
        /// <exception cref="System.InvalidOperationException">
        /// 未配置具有所请求名称的事务状态存储工厂或 Grain 存储提供程序。
        /// </exception>
        ITransactionalStateStorage<TState> Create<TState>(string? storageName, string stateName) where TState : class, new();
    }
}
