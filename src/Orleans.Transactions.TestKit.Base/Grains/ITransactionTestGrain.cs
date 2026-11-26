using System.Threading.Tasks;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 定义用于行使一个或多个交易整数状态的操作。
    /// </summary>
    public interface ITransactionTestGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 将每个交易状态设置为指定值。
        /// </summary>
        /// <param name="newValue">分配给每个国家的值。</param>
        /// <returns>一项代表交易操作的任务。</returns>
        [Transaction(TransactionOption.CreateOrJoin)]
        Task Set(int newValue);

        /// <summary>
        /// 在每个交易状态中添加一个值 。
        /// </summary>
        /// <param name="numberToAdd">每个国家的增值。</param>
        /// <returns>交易性国家命令中的最新价值。</returns>
        [Transaction(TransactionOption.CreateOrJoin)]
        Task<int[]> Add(int numberToAdd);

        /// <summary>
        /// 阅读每一个交易状态。
        /// </summary>
        /// <returns>交易状态命令中的当前值。</returns>
        [Transaction(TransactionOption.CreateOrJoin)]
        Task<int[]> Get();

        /// <summary>
        /// 阅读每个交易状态的承付值,不进行交易。
        /// </summary>
        /// <returns>承诺值。</returns>
        [Transaction(TransactionOption.Supported)]
        Task<int[]> GetCommitted();

        /// <summary>
        /// 给每个交易状态添加一个值, 然后为中止交易丢弃例外 。
        /// </summary>
        /// <param name="numberToAdd">扔之前要添加的值。</param>
        /// <returns>任务,在应用交易更新后总是有错误。</returns>
        [Transaction(TransactionOption.CreateOrJoin)]
        Task AddAndThrow(int numberToAdd);

        /// <summary>
        /// 设置每个交易状态, 然后放弃中止交易的例外 。
        /// </summary>
        /// <param name="numberToSet">扔掷前指定值。</param>
        /// <returns>任务,在应用交易更新后总是有错误。</returns>
        [Transaction(TransactionOption.CreateOrJoin)]
        Task SetAndThrow(int numberToSet);

        /// <summary>
        /// 要求在当前呼叫完成后停止启动 grain 。
        /// </summary>
        /// <returns>一旦要求解除功能,即完成一项任务。</returns>
        Task Deactivate();
    }
}
