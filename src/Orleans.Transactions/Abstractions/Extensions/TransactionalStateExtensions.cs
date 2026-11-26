using System;
using System.Threading.Tasks;

namespace Orleans.Transactions.Abstractions
{
    /// <summary>
    /// 提供与交易状态合作的便利方法。
    /// </summary>
    public static class TransactionalStateExtensions
    {
        /// <summary>
        /// 执行更新操作,不返回任何结果。
        /// </summary>
        /// <typeparam name="TState">交易状态类型。</typeparam>
        /// <param name="transactionalState">进行更新的交易状态。</param>
        /// <param name="updateAction">更新状态的行动。</param>
        /// <returns>代表更新操作的任务。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="transactionalState"/> 或 <paramref name="updateAction"/> 是 <see langword="null"/>。</exception>
        public static Task PerformUpdate<TState>(this ITransactionalState<TState> transactionalState, Action<TState> updateAction)
            where TState : class, new()
        {
            ArgumentNullException.ThrowIfNull(transactionalState);
            ArgumentNullException.ThrowIfNull(updateAction);

            return transactionalState.PerformUpdate<bool>(state => { updateAction(state); return true; });
        }
    }
}
