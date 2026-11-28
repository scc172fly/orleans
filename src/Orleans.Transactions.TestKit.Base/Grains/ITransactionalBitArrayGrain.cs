
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Orleans.Transactions.TestKit.Correctnesss
{
    /// <summary>
    /// 定义用于测试事务位数组状态的操作。
    /// </summary>
    public interface ITransactionalBitArrayGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 在不访问事务状态的情况下验证 Grain 可达。
        /// </summary>
        /// <returns>已完成的任务。</returns>
        Task Ping();

        /// <summary>
        /// 设置每个事务状态中的指定位。
        /// </summary>
        /// <param name="newValue">要设置的位索引（从零开始）。</param>
        /// <returns>表示事务操作的任务。</returns>
        [Transaction(TransactionOption.CreateOrJoin)]
        Task SetBit(int newValue);

        /// <summary>
        /// 读取每个事务位数组状态。
        /// </summary>
        /// <returns>按事务状态顺序排列的当前状态。</returns>
        [Transaction(TransactionOption.CreateOrJoin)]
        Task<List<BitArrayState>> Get();
    }
}
