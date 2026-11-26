namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 定义可读取独家锁定的交易整数状态操作。
    /// </summary>
    public interface IExclusiveLockTransactionTestGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 将交易状态设为指定值。
        /// </summary>
        /// <param name="newValue">国家应分配的值。</param>
        /// <returns>一项代表交易操作的任务。</returns>
        [Transaction(TransactionOption.CreateOrJoin)]
        Task Set(int newValue);

        /// <summary>
        /// 给交易状态添加一个值。
        /// </summary>
        /// <param name="numberToAdd">国家要增加的值。</param>
        /// <returns>包含更新状态值的数组。</returns>
        [Transaction(TransactionOption.CreateOrJoin)]
        Task<int[]> Add(int numberToAdd);

        /// <summary>
        /// 持有独家交易锁时读取交易状态。
        /// </summary>
        /// <returns>包含当前状态值的数组。</returns>
        [UseExclusiveLock]
        [Transaction(TransactionOption.CreateOrJoin)]
        Task<int[]> Get();
    }
}
