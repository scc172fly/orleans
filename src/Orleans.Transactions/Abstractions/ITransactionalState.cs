
using System;
using System.Threading.Tasks;

namespace Orleans.Transactions.Abstractions
{
    /// <summary>
    /// 遵循 Orleans 事务语义并支持
    /// 读写锁的状态。
    /// </summary>
    /// <typeparam name="TState">状态类型。</typeparam>
    public interface ITransactionalState<TState>
        where TState : class, new()
    {
        /// <summary>
        /// 执行读取操作并返回结果，不修改状态。
        /// </summary>
        /// <typeparam name="TResult">结果类型。</typeparam>
        /// <param name="readFunction">读取状态并返回结果的函数。该函数不得修改状态。</param>
        /// <returns>一个任务，其结果为 <paramref name="readFunction"/> 返回的值。</returns>
        Task<TResult> PerformRead<TResult>(Func<TState, TResult> readFunction);

        /// <summary>
        /// 按指定隔离级别执行读取操作并返回结果。
        /// </summary>
        /// <typeparam name="TResult">结果类型。</typeparam>
        /// <param name="readFunction">读取状态并返回结果的函数。该函数必须保持传入的状态不变。</param>
        /// <param name="isolation">读取隔离级别。</param>
        /// <returns>一个任务，其结果为 <paramref name="readFunction"/> 返回的值。</returns>
        /// <remarks>
        /// 结果会被独立复制。<see cref="ReadIsolation.ReadCommitted"/> 在没有环境事务的上下文中读取本地
        /// 最新已提交的状态。对于远程协调的写入，只有在参与者收到并持久化
        /// 提交确认后，写入结果才在本地可见。
        /// </remarks>
        Task<TResult> PerformRead<TResult>(Func<TState, TResult> readFunction, ReadIsolation isolation);

        /// <summary>
        /// 执行更新操作并返回结果。
        /// </summary>
        /// <typeparam name="TResult">结果类型。</typeparam>
        /// <param name="updateFunction">读取或更新状态并返回结果的函数。</param>
        /// <returns>一个任务，其结果为 <paramref name="updateFunction"/> 返回的值。</returns>
        Task<TResult> PerformUpdate<TResult>(Func<TState, TResult> updateFunction);
    }
}
