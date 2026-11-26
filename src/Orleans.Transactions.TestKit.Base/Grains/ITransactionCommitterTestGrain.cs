
using Orleans.Transactions.Abstractions;
using System.Threading.Tasks;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 定义远程招募的 grain 在当前交易中进行操作。
    /// </summary>
    public interface ITransactionCommitterTestGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 将当前交易中指定的操作列入列表。
        /// </summary>
        /// <param name="operation">交易实施时援引的操作。</param>
        /// <returns>代表征兵的任务</returns>
        [Transaction(TransactionOption.Join)]
        Task Commit(ITransactionCommitOperation<IRemoteCommitService> operation);
    }
}
