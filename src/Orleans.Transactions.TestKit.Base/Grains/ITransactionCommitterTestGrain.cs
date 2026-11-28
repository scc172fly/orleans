
using Orleans.Transactions.Abstractions;
using System.Threading.Tasks;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 定义将远程提交操作登记到当前事务中的 Grain。
    /// </summary>
    public interface ITransactionCommitterTestGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 将指定操作登记到当前事务中。
        /// </summary>
        /// <param name="operation">事务提交时要调用的操作。</param>
        /// <returns>表示登记操作的任务。</returns>
        [Transaction(TransactionOption.Join)]
        Task Commit(ITransactionCommitOperation<IRemoteCommitService> operation);
    }
}
