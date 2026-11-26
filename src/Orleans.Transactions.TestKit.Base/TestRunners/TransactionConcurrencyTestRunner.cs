using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 运行同时发生的交易设想方案,共享 grain 参与者。
    /// </summary>
    public abstract class TransactionConcurrencyTestRunner : TransactionTestRunnerBase
    {
        /// <summary>
        /// 初始化 <see cref="TransactionConcurrencyTestRunner"/> 类的新实例。
        /// </summary>
        /// <param name="grainFactory">下 grain 用于测试的工厂 grains.</param>
        /// <param name="output">用于写入测试输出的回调。</param>
        protected TransactionConcurrencyTestRunner(IGrainFactory grainFactory, Action<string> output)
        : base(grainFactory, output) { }

        /// <summary>
        /// 核证两个同时进行的交易,它们共同有一个 grain。
        /// </summary>
        /// <param name="grainStates">用于选择测试 grains的交易状态配置。</param>
        /// <returns>代表测试的任务。</returns>
        public virtual async Task SingleSharedGrainTest(string grainStates)
        {
            const int expected = 5;

            ITransactionTestGrain grain1 = RandomTestGrain(grainStates);
            ITransactionTestGrain grain2 = RandomTestGrain(grainStates);
            ITransactionTestGrain sharedGrain = RandomTestGrain(grainStates);
            List<ITransactionTestGrain> transaction1Members = new List<ITransactionTestGrain>(new[] { grain1, sharedGrain });
            List<ITransactionTestGrain> transaction2Members = new List<ITransactionTestGrain>(new[] { grain2, sharedGrain });

            ITransactionCoordinatorGrain coordinator1 = this.grainFactory.GetGrain<ITransactionCoordinatorGrain>(Guid.NewGuid());
            ITransactionCoordinatorGrain coordinator2 = this.grainFactory.GetGrain<ITransactionCoordinatorGrain>(Guid.NewGuid());
            await RunConcurrentTransactions(
                () => coordinator1.MultiGrainAdd(transaction1Members, expected),
                () => coordinator2.MultiGrainAdd(transaction2Members, expected));

            int[] actual = await grain1.Get();
            expected.Should().Be(actual.FirstOrDefault());
            actual = await grain2.Get();
            expected.Should().Be(actual.FirstOrDefault());
            actual = await sharedGrain.Get();
            actual.FirstOrDefault().Should().Be(expected * 2);
        }

        /// <summary>
        /// 验证相邻交易共有 grain的并行交易链。
        /// </summary>
        /// <param name="grainStates">用于选择测试 grains的交易状态配置。</param>
        /// <returns>代表测试的任务。</returns>
        public virtual async Task TransactionChainTest(string grainStates)
        {
            const int expected = 5;

            ITransactionTestGrain grain1 = RandomTestGrain(grainStates);
            ITransactionTestGrain grain2 = RandomTestGrain(grainStates);
            ITransactionTestGrain grain3 = RandomTestGrain(grainStates);
            ITransactionTestGrain grain4 = RandomTestGrain(grainStates);
            ITransactionTestGrain grain5 = RandomTestGrain(grainStates);
            List<ITransactionTestGrain> transaction1Members = new List<ITransactionTestGrain>(new[] { grain1, grain2 });
            List<ITransactionTestGrain> transaction2Members = new List<ITransactionTestGrain>(new[] { grain2, grain3 });
            List<ITransactionTestGrain> transaction3Members = new List<ITransactionTestGrain>(new[] { grain3, grain4 });
            List<ITransactionTestGrain> transaction4Members = new List<ITransactionTestGrain>(new[] { grain4, grain5 });

            ITransactionCoordinatorGrain coordinator1 = this.grainFactory.GetGrain<ITransactionCoordinatorGrain>(Guid.NewGuid());
            ITransactionCoordinatorGrain coordinator2 = this.grainFactory.GetGrain<ITransactionCoordinatorGrain>(Guid.NewGuid());
            ITransactionCoordinatorGrain coordinator3 = this.grainFactory.GetGrain<ITransactionCoordinatorGrain>(Guid.NewGuid());
            ITransactionCoordinatorGrain coordinator4 = this.grainFactory.GetGrain<ITransactionCoordinatorGrain>(Guid.NewGuid());
            await RunConcurrentTransactions(
                () => coordinator1.MultiGrainAdd(transaction1Members, expected),
                () => coordinator2.MultiGrainAdd(transaction2Members, expected),
                () => coordinator3.MultiGrainAdd(transaction3Members, expected),
                () => coordinator4.MultiGrainAdd(transaction4Members, expected));

            int[] actual = await grain1.Get();
            actual.FirstOrDefault().Should().Be(expected);
            actual = await grain2.Get();
            actual.FirstOrDefault().Should().Be(expected * 2);
            actual = await grain3.Get();
            actual.FirstOrDefault().Should().Be(expected * 2);
            actual = await grain4.Get();
            actual.FirstOrDefault().Should().Be(expected * 2);
            actual = await grain5.Get();
            actual.FirstOrDefault().Should().Be(expected);
        }

        /// <summary>
        /// 验证了一棵同时进行的交易,其中一项交易与其他两项交易的参与者共同参与。
        /// </summary>
        /// <param name="grainStates">用于选择测试 grains的交易状态配置。</param>
        /// <returns>代表测试的任务。</returns>
        public virtual async Task TransactionTreeTest(string grainStates)
        {
            const int expected = 5;

            ITransactionTestGrain grain1 = RandomTestGrain(grainStates);
            ITransactionTestGrain grain2 = RandomTestGrain(grainStates);
            ITransactionTestGrain grain3 = RandomTestGrain(grainStates);
            ITransactionTestGrain grain4 = RandomTestGrain(grainStates);
            List<ITransactionTestGrain> transaction1Members = new List<ITransactionTestGrain>(new[] { grain1, grain2 });
            List<ITransactionTestGrain> transaction2Members = new List<ITransactionTestGrain>(new[] { grain3, grain4 });
            List<ITransactionTestGrain> transaction3Members = new List<ITransactionTestGrain>(new[] { grain2, grain3 });

            ITransactionCoordinatorGrain coordinator1 = this.grainFactory.GetGrain<ITransactionCoordinatorGrain>(Guid.NewGuid());
            ITransactionCoordinatorGrain coordinator2 = this.grainFactory.GetGrain<ITransactionCoordinatorGrain>(Guid.NewGuid());
            ITransactionCoordinatorGrain coordinator3 = this.grainFactory.GetGrain<ITransactionCoordinatorGrain>(Guid.NewGuid());
            await RunConcurrentTransactions(
                () => coordinator1.MultiGrainAdd(transaction1Members, expected),
                () => coordinator2.MultiGrainAdd(transaction2Members, expected),
                () => coordinator3.MultiGrainAdd(transaction3Members, expected));

            int[] actual = await grain1.Get();
            actual.FirstOrDefault().Should().Be(expected);
            actual = await grain2.Get();
            actual.FirstOrDefault().Should().Be(expected * 2);
            actual = await grain3.Get();
            actual.FirstOrDefault().Should().Be(expected * 2);
            actual = await grain4.Get();
            actual.FirstOrDefault().Should().Be(expected);
        }

        private async Task RunConcurrentTransactions(params Func<Task>[] transactions)
        {
            var completed = await Task.WhenAll(transactions.Select(TryRunTransaction));
            // 第一次尝试时保留争议,然后按顺序排列重试,这样中止的行动就可以取得进展。
            for (var i = 0; i < transactions.Length; i++)
            {
                while (!completed[i])
                {
                    completed[i] = await TryRunTransaction(transactions[i]);
                }
            }
        }

        private async Task<bool> TryRunTransaction(Func<Task> transaction)
        {
            try
            {
                await transaction();
                return true;
            }
            catch (OrleansTransactionTransientFailureException exception)
            {
                this.testOutput($"Transaction aborted transiently: {exception.Message}. Retrying.");
                return false;
            }
        }
    }
}
