
using System;
using System.Threading.Tasks;

namespace Orleans.Transactions.Abstractions
{
    /// <summary>
    /// 定义事务提交时对服务执行的操作。
    /// </summary>
    /// <typeparam name="TService">服务类型。</typeparam>
    public interface ITransactionCommitOperation<TService>
        where TService : class
    {
        /// <summary>
        /// 作为事务提交的一部分，对服务执行该操作。
        /// </summary>
        /// <param name="transactionId">事务标识符。</param>
        /// <param name="service">要执行该操作的服务。</param>
        /// <returns>
        /// 返回一个任务：如果操作已完成且事务可以提交，则其结果为 <see langword="true"/>；
        /// 否则为 <see langword="false"/>。
        /// </returns>
        Task<bool> Commit(Guid transactionId, TService service);
    }

    /// <summary>
    /// 登记在所属事务提交时执行的服务操作。
    /// </summary>
    /// <typeparam name="TService">服务类型。</typeparam>
    public interface ITransactionCommitter<TService>
        where TService : class
    {
        /// <summary>
        /// 登记将在当前事务提交时执行的操作。
        /// </summary>
        /// <param name="operation">要登记的操作。</param>
        /// <returns>表示登记操作的任务。</returns>
        Task OnCommit(ITransactionCommitOperation<TService> operation);
    }
}
