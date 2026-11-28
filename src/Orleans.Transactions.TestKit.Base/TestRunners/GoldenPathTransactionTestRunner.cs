using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 执行涉及一个或多个 Grain 的正常事务场景。
    /// </summary>
    public abstract class GoldenPathTransactionTestRunner : TransactionTestRunnerBase
    {
        /// <summary>
        /// 初始化 <see cref="GoldenPathTransactionTestRunner"/> 类的新实例。
        /// </summary>
        /// <param name="grainFactory">用于访问测试 Grain 的 Grain 工厂。</param>
        /// <param name="output">用于写入测试输出的回调。</param>
        protected GoldenPathTransactionTestRunner(IGrainFactory grainFactory, Action<string> output)
        : base(grainFactory, output) { }

        /// <summary>
        /// 验证单 Grain 读取事务返回的初始状态。
        /// </summary>
        /// <param name="grainStates">用于选择测试 Grain 的事务状态配置。</param>
        /// <returns>表示该测试的任务。</returns>
        public virtual async Task SingleGrainReadTransaction(string grainStates)
        {
            const int expected = 0;

            ITransactionTestGrain grain = RandomTestGrain(grainStates);
            var actualResults = await grain.Get();
            // 每个事务状态都应为 0，因为尚未执行任何操作
            foreach (var actual in actualResults)
            {
                actual.Should().Be(expected);
            }
        }

        /// <summary>
        /// 验证单个 Grain 上成功执行的写入事务。
        /// </summary>
        /// <param name="grainStates">用于选择测试 Grain 的事务状态配置。</param>
        /// <returns>表示该测试的任务。</returns>
        public virtual async Task SingleGrainWriteTransaction(string grainStates)
        {
            const int delta = 5;
            ITransactionTestGrain grain = RandomTestGrain(grainStates);
            var original = await grain.Get();
            await grain.Add(delta);
            var expected = original.Select(value => value + delta).ToArray();
            var actual = await grain.Get();
            actual.Should().BeEquivalentTo(expected);
        }

        /// <summary>
        /// 验证跨多个 Grain 成功执行的写入事务。
        /// </summary>
        /// <param name="grainStates">用于选择测试 Grain 的事务状态配置。</param>
        /// <param name="grainCount">参与事务的 Grain 数量。</param>
        /// <returns>表示该测试的任务。</returns>
        public virtual async Task MultiGrainWriteTransaction(string grainStates, int grainCount)
        {
            const int expected = 5;

            List<ITransactionTestGrain> grains =
                Enumerable.Range(0, grainCount)
                    .Select(i => RandomTestGrain(grainStates))
                    .ToList();

            ITransactionCoordinatorGrain coordinator = this.grainFactory.GetGrain<ITransactionCoordinatorGrain>(Guid.NewGuid());

            await coordinator.MultiGrainAdd(grains, expected);

            foreach (var grain in grains)
            {
                var actualValues = await grain.Get();
                foreach (var actual in actualValues)
                {
                    actual.Should().Be(expected);
                }
            }
        }

        /// <summary>
        /// 验证跨多个 Grain 依次执行的设置和读改写操作。
        /// </summary>
        /// <param name="grainStates">用于选择测试 Grain 的事务状态配置。</param>
        /// <param name="grainCount">参与事务的 Grain 数量。</param>
        /// <returns>表示该测试的任务。</returns>
        public virtual async Task MultiGrainReadWriteTransaction(string grainStates, int grainCount)
        {
            const int delta = 5;

            List<ITransactionTestGrain> grains =
                Enumerable.Range(0, grainCount)
                    .Select(i => RandomTestGrain(grainStates))
                    .ToList();

            ITransactionCoordinatorGrain coordinator = this.grainFactory.GetGrain<ITransactionCoordinatorGrain>(Guid.NewGuid());

            await coordinator.MultiGrainSet(grains, delta);
            await coordinator.MultiGrainDouble(grains);

            int expected = delta + delta;
            foreach (var grain in grains)
            {
                int[] actualValues = await grain.Get();
                foreach (var actual in actualValues)
                {
                    if (expected != actual) this.testOutput($"{grain} - failed");
                    actual.Should().Be(expected);
                }
            }
        }

        /// <summary>
        /// 验证对固定 Grain 集合重复执行的读改写事务。
        /// </summary>
        /// <param name="grainStates">用于选择测试 Grain 的事务状态配置。</param>
        /// <param name="grainCount">参与每个事务的 Grain 数量。</param>
        /// <returns>表示该测试的任务。</returns>
        public virtual async Task RepeatGrainReadWriteTransaction(string grainStates, int grainCount)
        {
            const int repeat = 10;
            const int delta = 5;

            List<Guid> grainIds = Enumerable.Range(0, grainCount)
                    .Select(i => Guid.NewGuid())
                    .ToList();

            List<ITransactionTestGrain> grains = grainIds
                    .Select(id => TestGrain(grainStates, id))
                    .ToList();

            ITransactionCoordinatorGrain coordinator = this.grainFactory.GetGrain<ITransactionCoordinatorGrain>(Guid.NewGuid());

            await coordinator.MultiGrainSet(grains, delta);
            for (int i = 0; i < repeat; i++)
            {
                await coordinator.MultiGrainDouble(grains);

                int expected = delta * (int)Math.Pow(2, i + 1);
                foreach (var grain in grains)
                {
                    int[] actualValues = await grain.Get();
                    foreach (var actual in actualValues)
                    {
                        if (expected != actual) this.testOutput($"{grain} - failed");
                        actual.Should().Be(expected);
                    }
                }
            }
        }

        /// <summary>
        /// 验证同一个事务中对同一个 Grain 的多次写入均已应用。
        /// </summary>
        /// <param name="grainStates">用于选择测试 Grain 的事务状态配置。</param>
        /// <returns>表示该测试的任务。</returns>
        public virtual async Task MultiWriteToSingleGrainTransaction(string grainStates)
        {
            const int delta = 5;
            const int concurrentWrites = 3;

            ITransactionTestGrain grain = RandomTestGrain(grainStates);
            List<ITransactionTestGrain> grains = Enumerable.Repeat(grain, concurrentWrites).ToList();

            ITransactionCoordinatorGrain coordinator = this.grainFactory.GetGrain<ITransactionCoordinatorGrain>(Guid.NewGuid());

            await coordinator.MultiGrainAdd(grains, delta);

            int expected = delta * concurrentWrites;
            int[] actualValues = await grains[0].Get();
            foreach (var actual in actualValues)
            {
                actual.Should().Be(expected);
            }
        }

        /// <summary>
        /// 验证跨多个 Grain 执行的读-写-读-写访问序列。
        /// </summary>
        /// <param name="grainStates">用于选择测试 Grain 的事务状态配置。</param>
        /// <param name="grainCount">参与事务的 Grain 数量。</param>
        /// <returns>表示该测试的任务。</returns>
        public virtual async Task RWRWTest(string grainStates, int grainCount)
        {
            const int delta = 5;

            List<ITransactionTestGrain> grains =
                Enumerable.Range(0, grainCount)
                    .Select(i => RandomTestGrain(grainStates))
                    .ToList();

            ITransactionCoordinatorGrain coordinator = this.grainFactory.GetGrain<ITransactionCoordinatorGrain>(Guid.NewGuid());

            await coordinator.MultiGrainDoubleByRWRW(grains, delta);

            int expected = delta + delta;
            foreach (var grain in grains)
            {
                int[] actualValues = await grain.Get();
                foreach (var actual in actualValues)
                {
                    if (expected != actual) this.testOutput($"{grain} - failed");
                    actual.Should().Be(expected);
                }
            }
        }

        /// <summary>
        /// 验证跨多个 Grain 执行的写-读-写-读访问序列。
        /// </summary>
        /// <param name="grainStates">用于选择测试 Grain 的事务状态配置。</param>
        /// <param name="grainCount">参与事务的 Grain 数量。</param>
        /// <returns>表示该测试的任务。</returns>
        public virtual async Task WRWRTest(string grainStates, int grainCount)
        {
            const int delta = 5;

            List<ITransactionTestGrain> grains =
                Enumerable.Range(0, grainCount)
                    .Select(i => RandomTestGrain(grainStates))
                    .ToList();

            ITransactionCoordinatorGrain coordinator = this.grainFactory.GetGrain<ITransactionCoordinatorGrain>(Guid.NewGuid());

            await coordinator.MultiGrainDoubleByWRWR(grains, delta);

            int expected = delta + delta;
            foreach (var grain in grains)
            {
                int[] actualValues = await grain.Get();
                foreach (var actual in actualValues)
                {
                    if (expected != actual) this.testOutput($"{grain} - failed");
                    actual.Should().Be(expected);
                }
            }
        }

        /// <summary>
        /// 验证读已提交操作能够观测到已提交的写入。
        /// </summary>
        /// <param name="grainStates">用于选择测试 Grain 的事务状态配置。</param>
        /// <returns>表示该测试的任务。</returns>
        public virtual async Task SingleGrainReadCommitted(string grainStates)
        {
            const int expected = 7;

            ITransactionTestGrain grain = RandomTestGrain(grainStates);

            var initial = await grain.GetCommitted();
            initial.Should().AllSatisfy(value => value.Should().Be(0));

            await grain.Set(expected);

            // 非管理器参与者在收到确认后才提交，因此快照可能短暂滞后。
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            int[] actual;
            while ((actual = await grain.GetCommitted()).Any(value => value != expected) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            actual.Should().AllSatisfy(value => value.Should().Be(expected));
        }

        /// <summary>
        /// 验证在事务内部执行读已提交操作时会被拒绝。
        /// </summary>
        /// <param name="grainStates">用于选择测试 Grain 的事务状态配置。</param>
        /// <returns>表示该测试的任务。</returns>
        public virtual async Task ReadCommittedRejectedInsideTransaction(string grainStates)
        {
            ITransactionTestGrain grain = RandomTestGrain(grainStates);
            ITransactionCoordinatorGrain coordinator = this.grainFactory.GetGrain<ITransactionCoordinatorGrain>(Guid.NewGuid());

            Func<Task> task = () => coordinator.GetCommittedInTransaction(grain);
            await task.Should().ThrowAsync<OrleansTransactionException>();

            var actual = await grain.Get();
            actual.Should().AllSatisfy(value => value.Should().Be(0));
        }
    }
}
