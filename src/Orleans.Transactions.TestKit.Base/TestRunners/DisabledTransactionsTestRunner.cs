using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 验证禁用事务支持时的事务调用行为。
    /// </summary>
    public abstract class DisabledTransactionsTestRunner : TransactionTestRunnerBase
    {
        /// <summary>
        /// 初始化 <see cref="DisabledTransactionsTestRunner"/> 类的新实例。
        /// </summary>
        /// <param name="grainFactory">用于访问测试 Grain 的 Grain 工厂。</param>
        /// <param name="output">用于写入测试输出的回调。</param>
        protected DisabledTransactionsTestRunner(IGrainFactory grainFactory, Action<string> output)
        : base(grainFactory, output) { }

        /// <summary>
        /// 验证对事务 Grain 发起事务调用时会报告事务已禁用。
        /// </summary>
        /// <param name="transactionTestGrainClassName">要调用的事务测试 Grain 的类名。</param>
        public virtual void TransactionGrainsThrowWhenTransactions(string transactionTestGrainClassName)
        {
            const int delta = 5;
            ITransactionTestGrain grain = RandomTestGrain(transactionTestGrainClassName);
            Func<Task> task = () => grain.Set(delta);
            var response = task.Should().ThrowAsync<OrleansTransactionsDisabledException>();
        }

        /// <summary>
        /// 验证跨多个事务 Grain 发起事务调用时会报告事务已禁用。
        /// </summary>
        /// <param name="transactionTestGrainClassName">要调用的事务测试 Grain 的类名。</param>
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
