using System.Threading.Tasks;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 定义用于测试一个或多个事务整数状态的操作。
    /// </summary>
    public interface ITransactionTestGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 将所有事务状态设置为指定值。
        /// </summary>
        /// <param name="newValue">要赋给每个状态的值。</param>
        /// <returns>表示事务操作的任务。</returns>
        [Transaction(TransactionOption.CreateOrJoin)]
        Task Set(int newValue);

        /// <summary>
        /// 为每个事务状态增加指定数值。
        /// </summary>
        /// <param name="numberToAdd">要与每个状态值相加的数值。</param>
        /// <returns>按事务状态顺序排列的更新后值。</returns>
        [Transaction(TransactionOption.CreateOrJoin)]
        Task<int[]> Add(int numberToAdd);

        /// <summary>
        /// 读取每个事务状态。
        /// </summary>
        /// <returns>按事务状态顺序排列的当前值。</returns>
        [Transaction(TransactionOption.CreateOrJoin)]
        Task<int[]> Get();

        /// <summary>
        /// 在无事务情况下读取每个事务状态的已提交值。
        /// </summary>
        /// <returns>已提交的值。</returns>
        [Transaction(TransactionOption.Supported)]
        Task<int[]> GetCommitted();

        /// <summary>
        /// 为每个事务状态增加指定数值，随后引发异常以中止事务。
        /// </summary>
        /// <param name="numberToAdd">引发异常前要增加的数值。</param>
        /// <returns>应用事务更新后始终进入故障状态的任务。</returns>
        [Transaction(TransactionOption.CreateOrJoin)]
        Task AddAndThrow(int numberToAdd);

        /// <summary>
        /// 设置每个事务状态，随后引发异常以中止事务。
        /// </summary>
        /// <param name="numberToSet">引发异常前要赋予的值。</param>
        /// <returns>应用事务更新后始终进入故障状态的任务。</returns>
        [Transaction(TransactionOption.CreateOrJoin)]
        Task SetAndThrow(int numberToSet);

        /// <summary>
        /// 请求在当前调用完成后停用 Grain 激活实例。
        /// </summary>
        /// <returns>发出停用请求后已完成的任务。</returns>
        Task Deactivate();
    }
}
