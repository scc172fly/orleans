
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Orleans.Transactions.TestKit.Correctnesss
{
    /// <summary>
    /// 定义运行交易点数组状态的操作。
    /// </summary>
    public interface ITransactionalBitArrayGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 证明 grain 在无法进入交易状态的情况下是可以达到的。
        /// </summary>
        /// <returns>一项已完成的任务。</returns>
        Task Ping();

        /// <summary>
        /// 在每个交易状态下设置一点。
        /// </summary>
        /// <param name="newValue">零基位数指数要设置。</param>
        /// <returns>一项代表交易操作的任务。</returns>
        [Transaction(TransactionOption.CreateOrJoin)]
        Task SetBit(int newValue);

        /// <summary>
        /// 读取每一个交易性的微数状态。
        /// </summary>
        /// <returns>国家秩序中的现行状态</returns>
        [Transaction(TransactionOption.CreateOrJoin)]
        Task<List<BitArrayState>> Get();
    }
}
