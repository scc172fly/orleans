using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans.Concurrency;
using Orleans.Transactions.Abstractions;
using Orleans.Transactions.TestKit.Correctnesss;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 协调多个事务测试 Grain 集合之间的事务。
    /// </summary>
    public interface ITransactionCoordinatorGrain : IGrainWithGuidKey
    {
        /// <summary>
        /// 在新建事务中设置所有参与 Grain 的状态。
        /// </summary>
        /// <param name="grains">要更新其状态的 Grain。</param>
        /// <param name="numberToAdd">要赋给每个状态的值。</param>
        /// <returns>表示事务的任务。</returns>
        [Transaction(TransactionOption.Create)]
        Task MultiGrainSet(List<ITransactionTestGrain> grains, int numberToAdd);

        /// <summary>
        /// 在新建事务中为所有参与 Grain 的状态增加指定数值。
        /// </summary>
        /// <param name="grains">要更新其状态的 Grain。</param>
        /// <param name="numberToAdd">要与每个状态值相加的数值。</param>
        /// <returns>表示事务的任务。</returns>
        [Transaction(TransactionOption.Create)]
        Task MultiGrainAdd(List<ITransactionTestGrain> grains, int numberToAdd);

        /// <summary>
        /// 在新建事务中将每个参与 Grain 的第一个状态值加倍。
        /// </summary>
        /// <param name="grains">要将其状态值加倍的 Grain。</param>
        /// <returns>表示事务的任务。</returns>
        [Transaction(TransactionOption.Create)]
        Task MultiGrainDouble(List<ITransactionTestGrain> grains);

        /// <summary>
        /// 在新建事务中对所有参与 Grain 执行两次先读后写的操作序列。
        /// </summary>
        /// <param name="grains">要访问的 Grain。</param>
        /// <param name="numberToAdd">每次写入时要增加的数值。</param>
        /// <returns>表示事务的任务。</returns>
        [Transaction(TransactionOption.Create)]
        Task MultiGrainDoubleByRWRW(List<ITransactionTestGrain> grains, int numberToAdd);

        /// <summary>
        /// 在新建事务中对所有参与 Grain 执行两次先写后读的操作序列。
        /// </summary>
        /// <param name="grains">要访问的 Grain。</param>
        /// <param name="numberToAdd">每次写入时要增加的数值。</param>
        /// <returns>表示事务的任务。</returns>
        [Transaction(TransactionOption.Create)]
        Task MultiGrainDoubleByWRWR(List<ITransactionTestGrain> grains, int numberToAdd);

        /// <summary>
        /// 分叉当前事务但不发出 Grain 调用，从而留下孤立的事务分支。
        /// </summary>
        /// <returns>运行时检测到孤立事务分支时进入故障状态的任务。</returns>
        /// <exception cref="OrleansOrphanCallException">事务完成时仍存在孤立的调用分支。</exception>
        [Transaction(TransactionOption.Create)]
        Task OrphanCallTransaction();

        /// <summary>
        /// 在新建事务中更新 Grain，随后引发异常以中止事务。
        /// </summary>
        /// <param name="grain">要更新的 Grain。</param>
        /// <param name="numberToAdd">引发异常前要增加的数值。</param>
        /// <returns>更新后始终进入故障状态的任务。</returns>
        [Transaction(TransactionOption.Create)]
        Task AddAndThrow(ITransactionTestGrain grain, int numberToAdd);

        /// <summary>
        /// 更新指定 Grain 并调用会失败的更新操作，以中止新建事务。
        /// </summary>
        /// <param name="grain">更新操作会引发异常的 Grain。</param>
        /// <param name="grains">其更新在事务中止前完成的 Grain。</param>
        /// <param name="numberToAdd">要与每个状态值相加的数值。</param>
        /// <returns>调用会引发异常的 Grain 操作时进入故障状态的任务。</returns>
        [Transaction(TransactionOption.Create)]
        Task MultiGrainAddAndThrow(List<ITransactionTestGrain> grain, List<ITransactionTestGrain> grains, int numberToAdd);

        /// <summary>
        /// 在新建事务中设置所有参与的位数组 Grain 中的指定位。
        /// </summary>
        /// <param name="grains">要更新其位数组状态的 Grain。</param>
        /// <param name="bitIndex">要设置的位索引（从零开始）。</param>
        /// <returns>表示事务的任务。</returns>
        [Transaction(TransactionOption.Create)]
        Task MultiGrainSetBit(List<ITransactionalBitArrayGrain> grains, int bitIndex);

        /// <summary>
        /// 在新建事务中更新 Grain 状态并登记远程提交操作。
        /// </summary>
        /// <param name="committer">登记提交操作的 Grain。</param>
        /// <param name="operation">事务提交时要调用的操作。</param>
        /// <param name="grains">要更新其状态的 Grain。</param>
        /// <param name="numberToAdd">要与每个状态值相加的数值。</param>
        /// <returns>表示事务的任务。</returns>
        [Transaction(TransactionOption.Create)]
        Task MultiGrainAdd(ITransactionCommitterTestGrain committer, ITransactionCommitOperation<IRemoteCommitService> operation, List<ITransactionTestGrain> grains, int numberToAdd);

        /// <summary>
        /// 尝试从只读事务中更新事务状态。
        /// </summary>
        /// <param name="grains">其状态更新违反只读事务约束的 Grain。</param>
        /// <param name="numberToAdd">要增加的数值。</param>
        /// <returns>状态更新违反只读事务约束时进入故障状态的任务。</returns>
        /// <exception cref="OrleansReadOnlyViolatedException">Grain 尝试在只读事务中更新状态。</exception>
        [Transaction(TransactionOption.Create)]
        [ReadOnly]
        Task UpdateViolated(ITransactionTestGrain grains, int numberToAdd);

        /// <summary>
        /// 从事务内部对 Grain 执行读已提交操作。
        /// </summary>
        /// <param name="grain">要读取的 Grain。</param>
        /// <returns>已提交的值。</returns>
        [Transaction(TransactionOption.Create)]
        Task<int[]> GetCommittedInTransaction(ITransactionTestGrain grain);
    }
}
