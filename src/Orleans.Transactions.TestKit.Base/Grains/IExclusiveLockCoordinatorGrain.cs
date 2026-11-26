using System.Threading.Tasks;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 用于测试独家锁定的坐标 。
    /// </summary>
    public interface IExclusiveLockCoordinatorGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 读取并更新 grain ,无需获得阅读专用锁。
        /// </summary>
        /// <param name="grain">交易 grain 获得进入权。</param>
        /// <param name="value">读后添加的值。</param>
        /// <returns>代表交易的任务。</returns>
        [Transaction(TransactionOption.Create)]
        Task ReadThenWrite(ITransactionTestGrain grain, int value);

        /// <summary>
        /// 持有读取的独家锁时,读取并更新 grain 。
        /// </summary>
        /// <param name="grain">访问独家锁交易 grain 。</param>
        /// <param name="value">读后添加的值。</param>
        /// <returns>代表交易的任务。</returns>
        [Transaction(TransactionOption.Create)]
        Task ReadThenWriteWithExclusiveLock(IExclusiveLockTransactionTestGrain grain, int value);
    }
}
