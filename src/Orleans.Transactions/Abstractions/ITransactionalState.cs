
using System;
using System.Threading.Tasks;

namespace Orleans.Transactions.Abstractions
{
    /// <summary>
    /// 尊重 Orleans 交易语义并允许
    /// 读/ writer 锁定
    /// </summary>
    /// <typeparam name="TState">国家类型</typeparam>
    public interface ITransactionalState<TState>
        where TState : class, new()
    {
        /// <summary>
        /// 进行读取操作并返回结果,不修改状态。
        /// </summary>
        /// <typeparam name="TResult">结果类型。</typeparam>
        /// <param name="readFunction">函数读取状态并返回结果。该函数不得修改状态。</param>
        /// <returns>一项任务,其结果为 <paramref name="readFunction"/>返回的数值。</returns>
        Task<TResult> PerformRead<TResult>(Func<TState, TResult> readFunction);

        /// <summary>
        /// 执行带有指定隔离的读操作,并返回结果。
        /// </summary>
        /// <typeparam name="TResult">结果类型。</typeparam>
        /// <param name="readFunction">函数读取状态并返回结果。该函数必须保持所提供的状态不变。</param>
        /// <param name="isolation">读孤立。</param>
        /// <returns>一项任务,其结果为 <paramref name="readFunction"/>返回的数值。</returns>
        /// <remarks>
        /// 结果被独立复制。 <see cref="ReadIsolation.ReadCommitted"/> 读取本地最新
        /// 在一个空环境交易背景下指定状态。 远程协调的写作成为本地化
        /// 参与人收到确认书并持续承诺确认后即可看出。
        /// </remarks>
        Task<TResult> PerformRead<TResult>(Func<TState, TResult> readFunction, ReadIsolation isolation);

        /// <summary>
        /// 执行更新操作并返回结果。
        /// </summary>
        /// <typeparam name="TResult">结果类型。</typeparam>
        /// <param name="updateFunction">函数读取或更新状态并返回结果。</param>
        /// <returns>一项任务,其结果为 <paramref name="updateFunction"/>返回的数值。</returns>
        Task<TResult> PerformUpdate<TResult>(Func<TState, TResult> updateFunction);
    }
}
