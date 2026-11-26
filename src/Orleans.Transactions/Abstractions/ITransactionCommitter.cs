
using System;
using System.Threading.Tasks;

namespace Orleans.Transactions.Abstractions
{
    /// <summary>
    /// 定义交易发生时适用于服务的业务。
    /// </summary>
    /// <typeparam name="TService">服务类型。</typeparam>
    public interface ITransactionCommitOperation<TService>
        where TService : class
    {
        /// <summary>
        /// 作为交易的一部分,业务适用于服务。
        /// </summary>
        /// <param name="transactionId">交易识别特征。</param>
        /// <param name="service">操作所应用的服务 。</param>
        /// <returns>
        /// 任务的结果是完成业务和交易可进行时的 <see langword="true"/> ;
        /// 否则, <see langword="false"/>.
        /// </returns>
        Task<bool> Commit(Guid transactionId, TService service);
    }

    /// <summary>
    /// 列入交易时适用的服务业务清单。
    /// </summary>
    /// <typeparam name="TService">服务类型。</typeparam>
    public interface ITransactionCommitter<TService>
        where TService : class
    {
        /// <summary>
        /// 输入当当前交易发生时要应用的操作。
        /// </summary>
        /// <param name="operation">作战行动</param>
        /// <returns>一项代表征兵行动的任务。</returns>
        Task OnCommit(ITransactionCommitOperation<TService> operation);
    }
}
