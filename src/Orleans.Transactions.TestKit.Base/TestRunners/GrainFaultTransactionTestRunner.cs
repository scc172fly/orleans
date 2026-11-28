using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 执行测试参与 Grain 引发失败的事务场景。
    /// </summary>
    public abstract class GrainFaultTransactionTestRunner : TransactionTestRunnerBase
    {
        /// <summary>
        /// 初始化 <see cref="GrainFaultTransactionTestRunner"/> 类的新实例。
        /// </summary>
        /// <param name="grainFactory">用于访问测试 Grain 的 Grain 工厂。</param>
        /// <param name="output">用于写入测试输出的回调。</param>
        public GrainFaultTransactionTestRunner(IGrainFactory grainFactory, Action<string> output)
        : base(grainFactory, output)
        { }

        /// <summary>
        /// 验证 Grain 异常会中止其事务而不改变已提交状态。
        /// </summary>
        /// <param name="grainStates">用于选择测试 Grain 的事务状态配置。</param>
        /// <returns>表示该测试的任务。</returns>
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
        /// 验证只读事务中的写入尝试会被拒绝且不会改变已提交状态。
        /// </summary>
        /// <param name="grainStates">用于选择测试 Grain 的事务状态配置。</param>
        /// <returns>表示该测试的任务。</returns>
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
        /// 验证单个参与者引发的异常会导致多 Grain 事务原子性中止。
        /// </summary>
        /// <param name="grainStates">用于选择测试 Grain 的事务状态配置。</param>
        /// <returns>表示该测试的任务。</returns>
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
        /// 验证多 Grain 事务中止时会将一个导致失败的根因 Grain 异常报告为内部异常。
        /// </summary>
        /// <param name="grainStates">用于选择测试 Grain 的事务状态配置。</param>
        /// <returns>表示该测试的任务。</returns>
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
        /// 验证孤立调用会中止其事务而不改变已提交状态。
        /// </summary>
        /// <param name="grainStates">用于选择测试 Grain 的事务状态配置。</param>
        /// <returns>表示该测试的任务。</returns>
        public virtual async Task AbortTransactionOnOrphanCalls(string grainStates)
        {
            const int expected = 5;

            ITransactionTestGrain grain = RandomTestGrain(grainStates);
            ITransactionCoordinatorGrain coordinator = this.grainFactory.GetGrain<ITransactionCoordinatorGrain>(Guid.NewGuid());

            await grain.Set(expected);
            Func<Task> task = () => coordinator.OrphanCallTransaction();
            await task.Should().ThrowAsync<OrleansOrphanCallException>();

            //await Task.Delay(20000); // give time for GC

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
                    // 由于采用乐观读取，我们可能读到已中止事务的状态，
                    // 从而导致级联中止
                }
            }
        }
    }
}
