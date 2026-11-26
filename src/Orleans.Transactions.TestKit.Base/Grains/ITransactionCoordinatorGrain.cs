using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans.Concurrency;
using Orleans.Transactions.Abstractions;
using Orleans.Transactions.TestKit.Correctnesss;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 协调交易测试 grains各套收集的交易。
    /// </summary>
    public interface ITransactionCoordinatorGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 设置新创建的交易中所有参与 grain 状态。
        /// </summary>
        /// <param name="grains">下 grains 其州得到更新。</param>
        /// <param name="numberToAdd">分配给每个国家的值。</param>
        /// <returns>代表交易的任务。</returns>
        [Transaction(TransactionOption.Create)]
        Task MultiGrainSet(List<ITransactionTestGrain> grains, int numberToAdd);

        /// <summary>
        /// 将新创建的交易中所有参与 grain 的国家添加一个值。
        /// </summary>
        /// <param name="grains">下 grains 其州得到更新。</param>
        /// <param name="numberToAdd">每个国家的增值。</param>
        /// <returns>代表交易的任务。</returns>
        [Transaction(TransactionOption.Create)]
        Task MultiGrainAdd(List<ITransactionTestGrain> grains, int numberToAdd);

        /// <summary>
        /// 将新创建的交易中每个参与交易的 grain 的第一个状态值翻一番。
        /// </summary>
        /// <param name="grains">下 grains 州级翻倍</param>
        /// <returns>代表交易的任务。</returns>
        [Transaction(TransactionOption.Create)]
        Task MultiGrainDouble(List<ITransactionTestGrain> grains);

        /// <summary>
        /// 对新建交易中的所有参与 grains 应用两个读写序列。
        /// </summary>
        /// <param name="grains">下 grains 获得进入权。</param>
        /// <param name="numberToAdd">每次写作时要添加的值 。</param>
        /// <returns>代表交易的任务。</returns>
        [Transaction(TransactionOption.Create)]
        Task MultiGrainDoubleByRWRW(List<ITransactionTestGrain> grains, int numberToAdd);

        /// <summary>
        /// 对新建交易中的所有参与 grains 应用两个写读序列。
        /// </summary>
        /// <param name="grains">下 grains 获得进入权。</param>
        /// <param name="numberToAdd">每次写作时要添加的值 。</param>
        /// <returns>代表交易的任务。</returns>
        [Transaction(TransactionOption.Create)]
        Task MultiGrainDoubleByWRWR(List<ITransactionTestGrain> grains, int numberToAdd);

        /// <summary>
        /// 将当前交易叉开,不发出 grain 电话,留下一个孤儿交易分支。
        /// </summary>
        /// <returns>任务, 运行时发现孤儿交易分支时有误差。</returns>
        /// <exception cref="OrleansOrphanCallException">交易与一个孤立的呼叫分支完成。</exception>
        [Transaction(TransactionOption.Create)]
        Task OrphanCallTransaction();

        /// <summary>
        /// 更新 grain ,然后放弃中止新建交易的例外。
        /// </summary>
        /// <param name="grain">下 grain 更新。</param>
        /// <param name="numberToAdd">扔之前要添加的值。</param>
        /// <returns>更新后总是有故障的任务。</returns>
        [Transaction(TransactionOption.Create)]
        Task AddAndThrow(ITransactionTestGrain grain, int numberToAdd);

        /// <summary>
        /// 更新提供的新 grains ,并引用错误的更新中止新建的交易。
        /// </summary>
        /// <param name="grain">其更新操作的 grains 。</param>
        /// <param name="grains">交易中止前更新完成的 grains 。</param>
        /// <param name="numberToAdd">每个国家的增值。</param>
        /// <returns>任务在投掷 grain 操作时发生故障。</returns>
        [Transaction(TransactionOption.Create)]
        Task MultiGrainAddAndThrow(List<ITransactionTestGrain> grain, List<ITransactionTestGrain> grains, int numberToAdd);

        /// <summary>
        /// 设置新创建的交易中所有参与的位数数组 grains 中的位数 。
        /// </summary>
        /// <param name="grains">下 grains 其位数状态更新。</param>
        /// <param name="bitIndex">零基位数指数要设置。</param>
        /// <returns>代表交易的任务。</returns>
        [Transaction(TransactionOption.Create)]
        Task MultiGrainSetBit(List<ITransactionalBitArrayGrain> grains, int bitIndex);

        /// <summary>
        /// 更新 grain 状态,并招募远程操作者在新创建的交易中进行操作。
        /// </summary>
        /// <param name="committer">下 grain 并征召执事者,</param>
        /// <param name="operation">交易实施时援引的操作。</param>
        /// <param name="grains">下 grains 其州得到更新。</param>
        /// <param name="numberToAdd">每个国家的增值。</param>
        /// <returns>代表交易的任务。</returns>
        [Transaction(TransactionOption.Create)]
        Task MultiGrainAdd(ITransactionCommitterTestGrain committer, ITransactionCommitOperation<IRemoteCommitService> operation, List<ITransactionTestGrain> grains, int numberToAdd);

        /// <summary>
        /// 尝试更新只读交易的交易状态 。
        /// </summary>
        /// <param name="grains">下 grain 其更新情况违反了只读交易。</param>
        /// <param name="numberToAdd">增加值。</param>
        /// <returns>任务,当国家更新违反只读交易时有故障。</returns>
        /// <exception cref="OrleansReadOnlyViolatedException">下 grain 尝试在只读交易中更新状态。</exception>
        [Transaction(TransactionOption.Create)]
        [ReadOnly]
        Task UpdateViolated(ITransactionTestGrain grains, int numberToAdd);

        /// <summary>
        /// 执行从交易内部读取 grain 的重写。
        /// </summary>
        /// <param name="grain"> grain 改为。</param>
        /// <returns>承诺值。</returns>
        [Transaction(TransactionOption.Create)]
        Task<int[]> GetCommittedInTransaction(ITransactionTestGrain grain);
    }
}
