using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 当交易支持被禁用时验证交易电话。
    /// </summary>
    public abstract class DisabledTransactionsTestRunner : TransactionTestRunnerBase
    {
        /// <summary>
        /// 初始化 <see cref="DisabledTransactionsTestRunner"/> 类的新实例。
        /// </summary>
        /// <param name="grainFactory">下 grain 用于测试的工厂 grains.</param>
        /// <param name="output">用于写入测试输出的回调。</param>
        protected DisabledTransactionsTestRunner(IGrainFactory grainFactory, Action<string> output)
        : base(grainFactory, output) { }

        /// <summary>
        /// 证明援引交易 grain 报告交易已停止。
        /// </summary>
        /// <param name="transactionTestGrainClassName">交易测试的类名 grain 援引。</param>
        public virtual void TransactionGrainsThrowWhenTransactions(string transactionTestGrainClassName)
        {
            const int delta = 5;
            ITransactionTestGrain grain = RandomTestGrain(transactionTestGrainClassName);
            Func<Task> task = () => grain.Set(delta);
            var response = task.Should().ThrowAsync<OrleansTransactionsDisabledException>();
        }

        /// <summary>
        /// 证明多次交易 grains 中援引交易报告已禁用。
        /// </summary>
        /// <param name="transactionTestGrainClassName">交易测试的类名 grains 援引。</param>
        public virtual void MultiTransactionGrainsThrowWhenTransactions(string transactionTestGrainClassName)
        {
            const int delta = 5;
            const int grainCount = TransactionTestConstants.MaxCoordinatedTransactions;

            List<ITransactionTestGrain> grains =
                Enumerable.Range(0, grainCount)
                    .Select(i => RandomTestGrain(transactionTestGrainClassName))
                    .ToList();
            ITransactionCoordinatorGrain coordinator = this.grainFactory.GetGrain<ITransactionCoordinatorGrain>(Guid.NewGuid());

            Func<Task> task = () => coordinator.MultiGrainSet(grains, delta);
            var response = task.Should().ThrowAsync<OrleansTransactionsDisabledException>();
        }
    }
}
