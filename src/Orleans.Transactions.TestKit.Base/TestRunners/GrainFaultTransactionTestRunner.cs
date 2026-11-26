using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 运行交易情景,以证明参与 grains引起的故障。
    /// </summary>
    public abstract class GrainFaultTransactionTestRunner : TransactionTestRunnerBase
    {
        /// <summary>
        /// 初始化 <see cref="GrainFaultTransactionTestRunner"/> 类的新实例。
        /// </summary>
        /// <param name="grainFactory">下 grain 用于测试的工厂 grains.</param>
        /// <param name="output">用于写入测试输出的回调。</param>
        public GrainFaultTransactionTestRunner(IGrainFactory grainFactory, Action<string> output)
        : base(grainFactory, output)
        { }

        /// <summary>
        /// 证明 grain 例外规定中止其交易而不改变承诺状态。
        /// </summary>
        /// <param name="grainStates">用于选择测试 grain的交易状态配置。</param>
        /// <returns>代表测试的任务。</returns>
        public virtual async Task AbortTransactionOnExceptions(string grainStates)
        {
            const int expected = 5;

            ITransactionTestGrain grain = RandomTestGrain(grainStates);
            ITransactionCoordinatorGrain coordinator = this.grainFactory.GetGrain<ITransactionCoordinatorGrain>(Guid.NewGuid());

            await coordinator.MultiGrainSet(new List<ITransactionTestGrain> { grain }, expected);
            Func<Task> task = () => coordinator.AddAndThrow(grain, expected);
            await task.Should().ThrowAsync<OrleansTransactionAbortedException>();

            await TestAfterDustSettles(async () =>
            {
                int[] actualValues = await grain.Get();
                foreach (var actual in actualValues)
                {
                    actual.Should().Be(expected);
                }
            });
        }

        /// <summary>
        /// 证明只读交易中试图写作的拒绝不改变承诺状态。
        /// </summary>
        /// <param name="grainStates">用于选择测试 grain的交易状态配置。</param>
        /// <returns>代表测试的任务。</returns>
        public virtual async Task AbortTransactionOnReadOnlyViolatedException(string grainStates)
        {
            const int expected = 5;

            ITransactionTestGrain grain = RandomTestGrain(grainStates);
            ITransactionCoordinatorGrain coordinator = this.grainFactory.GetGrain<ITransactionCoordinatorGrain>(Guid.NewGuid());

            await coordinator.MultiGrainSet(new List<ITransactionTestGrain> { grain }, expected);
            Func<Task> task = () => coordinator.UpdateViolated(grain, expected);
            await task.Should().ThrowAsync<OrleansReadOnlyViolatedException>();

            await TestAfterDustSettles(async () =>
            {
                int[] actualValues = await grain.Get();
                foreach (var actual in actualValues)
                {
                    actual.Should().Be(expected);
                }
            });
        }

        /// <summary>
        /// 证明一名参与人的例外情形会以原子方式中止多grain 交易。
        /// </summary>
        /// <param name="grainStates">用于选择测试 grains的交易状态配置。</param>
        /// <returns>代表测试的任务。</returns>
        public virtual async Task MultiGrainAbortTransactionOnExceptions(string grainStates)
        {
            const int grainCount = TransactionTestConstants.MaxCoordinatedTransactions - 1;
            const int expected = 5;

            ITransactionTestGrain throwGrain = RandomTestGrain(grainStates);
            List<ITransactionTestGrain> grains =
                Enumerable.Range(0, grainCount)
                    .Select(i => RandomTestGrain(grainStates))
                    .ToList();
            ITransactionCoordinatorGrain coordinator = this.grainFactory.GetGrain<ITransactionCoordinatorGrain>(Guid.NewGuid());

            await throwGrain.Set(expected);
            await coordinator.MultiGrainSet(grains, expected);
            Func<Task> task = () => coordinator.MultiGrainAddAndThrow(new List<ITransactionTestGrain>()
            {
                throwGrain
            }, grains, expected);
            await task.Should().ThrowAsync<OrleansTransactionAbortedException>();
            grains.Add(throwGrain);

            await TestAfterDustSettles(async () =>
            {
                foreach (var grain in grains)
                {
                    int[] actualValues = await grain.Get();
                    foreach (var actual in actualValues)
                    {
                        actual.Should().Be(expected);
                    }
                }
            });
        }

        /// <summary>
        /// 证明多grain 中止报告有一个根原因 grain 例外,作为内部例外。
        /// </summary>
        /// <param name="grainStates">用于选择测试 grains的交易状态配置。</param>
        /// <returns>代表测试的任务。</returns>
        public virtual async Task AbortTransactionExceptionInnerExceptionOnlyContainsOneRootCauseException(string grainStates)
        {
            const int throwGrainCount = 3;
            const int grainCount = TransactionTestConstants.MaxCoordinatedTransactions - throwGrainCount;
            const int expected = 5;

            List<ITransactionTestGrain> throwGrains = Enumerable.Range(0, throwGrainCount)
                .Select(i => RandomTestGrain(grainStates))
                .ToList();
            List<ITransactionTestGrain> grains =
                Enumerable.Range(0, grainCount)
                    .Select(i => RandomTestGrain(grainStates))
                    .ToList();
            ITransactionCoordinatorGrain coordinator = this.grainFactory.GetGrain<ITransactionCoordinatorGrain>(Guid.NewGuid());

            await coordinator.MultiGrainSet(throwGrains, expected);
            await coordinator.MultiGrainSet(grains, expected);

            async Task InnerExceptionCheck()
            {
                try
                {
                    await coordinator.MultiGrainAddAndThrow(throwGrains, grains, expected);
                }
                catch (Exception e)
                {
                    e.InnerException.Should().BeOfType<AddAndThrowException>();
                    throw;
                }
            }

            Func<Task> task = () => InnerExceptionCheck();
            await task.Should().ThrowAsync<OrleansTransactionAbortedException>();

            grains.AddRange(throwGrains);

            await TestAfterDustSettles(async () =>
            {
                foreach (var grain in grains)
                {
                    int[] actualValues = await grain.Get();
                    foreach (var actual in actualValues)
                    {
                        actual.Should().Be(expected);
                    }
                }
            });
        }

        /// <summary>
        /// 证明一个孤儿的呼唤会中止其交易,而不改变承诺状态。
        /// </summary>
        /// <param name="grainStates">用于选择测试 grain的交易状态配置。</param>
        /// <returns>代表测试的任务。</returns>
        public virtual async Task AbortTransactionOnOrphanCalls(string grainStates)
        {
            const int expected = 5;

            ITransactionTestGrain grain = RandomTestGrain(grainStates);
            ITransactionCoordinatorGrain coordinator = this.grainFactory.GetGrain<ITransactionCoordinatorGrain>(Guid.NewGuid());

            await grain.Set(expected);
            Func<Task> task = () => coordinator.OrphanCallTransaction();
            await task.Should().ThrowAsync<OrleansOrphanCallException>();

            //等待 Task.Delay(2000年) ;/给GC留有时间

            await TestAfterDustSettles(async () =>
            {
                int[] actualValues = await grain.Get();
                foreach (var actual in actualValues)
                {
                    actual.Should().Be(expected);
                }
            });
        }

        private static async Task TestAfterDustSettles(Func<Task> what)
        {
            int tries = 2;
            while (tries-- > 0)
            {
                try
                {
                    await what();
                }
                catch (OrleansCascadingAbortException)
                {
                    // 是因为阅读结果乐观,我们可能读到中止交易的状态
                    // 导致串列中止
                }
            }
        }
    }
}
