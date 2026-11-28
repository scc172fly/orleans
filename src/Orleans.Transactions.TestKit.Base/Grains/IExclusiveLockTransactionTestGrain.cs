namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 定义读取时获取排他锁的事务整数状态操作。
    /// </summary>
    public interface IExclusiveLockTransactionTestGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 将事务状态设置为指定值。
        /// </summary>
        /// <param name="newValue">要赋给状态的值。</param>
        /// <returns>表示事务操作的任务。</returns>
        [Transaction(TransactionOption.CreateOrJoin)]
        Task Set(int newValue);

        /// <summary>
        /// 为事务状态增加指定数值。
        /// </summary>
        /// <param name="numberToAdd">要为状态增加的数值。</param>
        /// <returns>包含更新后状态值的数组。</returns>
        [Transaction(TransactionOption.CreateOrJoin)]
        Task<int[]> Add(int numberToAdd);

        /// <summary>
        /// 持有事务排他锁时读取事务状态。
        /// </summary>
        /// <returns>包含当前状态值的数组。</returns>
        [UseExclusiveLock]
        [Transaction(TransactionOption.CreateOrJoin)]
        Task<int[]> Get();
    }
}
