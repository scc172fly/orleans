
using Orleans.Runtime;

namespace Orleans.Transactions.Abstractions
{
    /// <summary>
    /// 为 Grain 激活实例创建事务状态存储实例。
    /// </summary>
    public interface ITransactionalStateStorageFactory
    {
        /// <summary>
        /// 为指定的事务状态创建存储。
        /// </summary>
        /// <typeparam name="TState">事务状态类型。</typeparam>
        /// <param name="stateName">事务状态名称。</param>
        /// <param name="context">拥有该状态的 Grain 上下文。</param>
        /// <returns>事务状态存储实例。</returns>
        ITransactionalStateStorage<TState> Create<TState>(string stateName, IGrainContext context) where TState : class, new();
    }
}
