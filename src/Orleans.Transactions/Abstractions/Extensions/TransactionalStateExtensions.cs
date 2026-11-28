using System;
using System.Threading.Tasks;

namespace Orleans.Transactions.Abstractions
{
    /// <summary>
    /// 提供用于操作事务状态的便捷方法。
    /// </summary>
    public static class TransactionalStateExtensions
    {
        /// <summary>
        /// 执行更新操作，不返回结果。
        /// </summary>
        /// <typeparam name="TState">事务状态类型。</typeparam>
        /// <param name="transactionalState">要执行更新的事务状态。</param>
        /// <param name="updateAction">更新状态的操作委托。</param>
        /// <returns>表示更新操作的任务。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="transactionalState"/> 或 <paramref name="updateAction"/> 为 <see langword="null"/>。</exception>
        public static Task PerformUpdate<TState>(this ITransactionalState<TState> transactionalState, Action<TState> updateAction)
            where TState : class, new()
        {
            ArgumentNullException.ThrowIfNull(transactionalState);
            ArgumentNullException.ThrowIfNull(updateAction);

            return transactionalState.PerformUpdate<bool>(state => { updateAction(state); return true; });
        }
    }
}
