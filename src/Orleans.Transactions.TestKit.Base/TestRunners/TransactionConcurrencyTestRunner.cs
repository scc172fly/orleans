using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 执行存在共享 Grain 参与者的并发事务场景。
    /// </summary>
    public abstract class TransactionConcurrencyTestRunner : TransactionTestRunnerBase
    {
        /// <summary>
        /// 初始化 <see cref="TransactionConcurrencyTestRunner"/> 类的新实例。
        /// </summary>
        /// <param name="grainFactory">用于访问测试 Grain 的 Grain 工厂。</param>
        /// <param name="output">用于写入测试输出的回调。</param>
        protected TransactionConcurrencyTestRunner(IGrainFactory grainFactory, Action<string> output)
        : base(grainFactory, output) { }

        /// <summary>
        /// 验证两个共享同一个 Grain 的并发事务。
        /// </summary>
        /// <param name="grainStates">用于选择测试 Grain 的事务状态配置。</param>
        /// <returns>表示该测试的任务。</returns>
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
        /// 验证一组链式并发事务，其中相邻事务共享一个 Grain。
        /// </summary>
        /// <param name="grainStates">用于选择测试 Grain 的事务状态配置。</param>
        /// <returns>表示该测试的任务。</returns>
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
        /// 验证一组树形并发事务，其中一个事务汇集另外两个事务的参与者。
        /// </summary>
        /// <param name="grainStates">用于选择测试 Grain 的事务状态配置。</param>
        /// <returns>表示该测试的任务。</returns>
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
            // 在首次尝试中保留争用，之后串行执行重试，使已中止操作能够继续推进。
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
