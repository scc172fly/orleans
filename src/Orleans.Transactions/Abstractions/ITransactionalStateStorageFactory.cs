
using Orleans.Runtime;

namespace Orleans.Transactions.Abstractions
{
    /// <summary>
    /// 创建用于启动 grain 的交易状态存储实例。
    /// </summary>
    public interface ITransactionalStateStorageFactory
    {
        /// <summary>
        /// 为指定的交易状态创建存储。
        /// </summary>
        /// <typeparam name="TState">交易状态类型。</typeparam>
        /// <param name="stateName">交易国名称。</param>
        /// <param name="context">下 grain 状态所在的背景。</param>
        /// <returns>交易性国家储存实例。</returns>
        ITransactionalStateStorage<TState> Create<TState>(string stateName, IGrainContext context) where TState : class, new();
    }
}
