using System.Threading.Tasks;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 协调用于测试排他锁的先读后写事务模式。
    /// </summary>
    public interface IExclusiveLockCoordinatorGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 在读取时不获取排他锁的情况下，先读取再更新 Grain。
        /// </summary>
        /// <param name="grain">要访问的事务 Grain。</param>
        /// <param name="value">读取后要增加的数值。</param>
        /// <returns>表示事务的任务。</returns>
        [Transaction(TransactionOption.Create)]
        Task ReadThenWrite(ITransactionTestGrain grain, int value);

        /// <summary>
        /// 在持有读取时获取的排他锁的情况下，先读取再更新 Grain。
        /// </summary>
        /// <param name="grain">要访问的使用排他锁的事务 Grain。</param>
        /// <param name="value">读取后要增加的数值。</param>
        /// <returns>表示事务的任务。</returns>
        [Transaction(TransactionOption.Create)]
        Task ReadThenWriteWithExclusiveLock(IExclusiveLockTransactionTestGrain grain, int value);
    }
}
