using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 运行一个或多个 grains的成功交易情景。
    /// </summary>
    public abstract class GoldenPathTransactionTestRunner : TransactionTestRunnerBase
    {
        /// <summary>
        /// 初始化 <see cref="GoldenPathTransactionTestRunner"/> 类的新实例。
        /// </summary>
        /// <param name="grainFactory">下 grain 用于测试的工厂 grains.</param>
        /// <param name="output">用于写入测试输出的回调。</param>
        protected GoldenPathTransactionTestRunner(IGrainFactory grainFactory, Action<string> output)
        : base(grainFactory, output) { }

        /// <summary>
        /// 验证以单项grain 读作交易形式退回的初始状态。
        /// </summary>
        /// <param name="grainStates">用于选择测试 grain的交易状态配置。</param>
        /// <returns>代表测试的任务。</returns>
        public virtual async Task SingleGrainReadTransaction(string grainStates)
        {
            const int expected = 0;

            ITransactionTestGrain grain = RandomTestGrain(grainStates);
            var actualResults = await grain.Get();
            //每个交易国都应为0,因为尚未实施任何业务
            foreach (var actual in actualResults)
            {
                actual.Should().Be(expected);
            }
        }

        /// <summary>
        /// 在一个单 grain上验证成功的书面交易。
        /// </summary>
        /// <param name="grainStates">用于选择测试 grain的交易状态配置。</param>
        /// <returns>代表测试的任务。</returns>
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
        /// 验证多个 grains之间成功的写作交易。
        /// </summary>
        /// <param name="grainStates">用于选择测试 grains的交易状态配置。</param>
        /// <param name="grainCount">编号数 grains 参与交易。</param>
        /// <returns>代表测试的任务。</returns>
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
        /// 连续验证设置的和在多个 grains之间进行读修改- writer操作。
        /// </summary>
        /// <param name="grainStates">用于选择测试 grains的交易状态配置。</param>
        /// <param name="grainCount">编号数 grains 参与交易。</param>
        /// <returns>代表测试的任务。</returns>
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
        /// 验证一套固定的 grains的重复读修改- write交易。
        /// </summary>
        /// <param name="grainStates">用于选择测试 grains的交易状态配置。</param>
        /// <param name="grainCount">编号数 grains 参与每项交易。</param>
        /// <returns>代表测试的任务。</returns>
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
        /// 证明在一个交易中将多个字母写到同一个 grain ,全部适用。
        /// </summary>
        /// <param name="grainStates">用于选择测试 grain的交易状态配置。</param>
        /// <returns>代表测试的任务。</returns>
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
        /// 验证一个跨越多个 grains的读写读写访问序列。
        /// </summary>
        /// <param name="grainStates">用于选择测试 grains的交易状态配置。</param>
        /// <param name="grainCount">编号数 grains 参与交易。</param>
        /// <returns>代表测试的任务。</returns>
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
        /// 验证多个 grains的写读访问序列。
        /// </summary>
        /// <param name="grainStates">用于选择测试 grains的交易状态配置。</param>
        /// <param name="grainCount">编号数 grains 参与交易。</param>
        /// <returns>代表测试的任务。</returns>
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
        /// 证明经重新确认的读者会遵守承诺书。
        /// </summary>
        /// <param name="grainStates">用于选择测试 grain的交易状态配置。</param>
        /// <returns>代表测试的任务。</returns>
        public virtual async Task SingleGrainReadCommitted(string grainStates)
        {
            const int expected = 7;

            ITransactionTestGrain grain = RandomTestGrain(grainStates);

            var initial = await grain.GetCommitted();
            initial.Should().AllSatisfy(value => value.Should().Be(0));

            await grain.Set(expected);

            // 非管理参与方承诺在确认抵达后即予确认,因此快照可能会短暂滞后。
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            int[] actual;
            while ((actual = await grain.GetCommitted()).Any(value => value != expected) && DateTime.UtcNow < deadline)
            {
                await Task.Delay(50);
            }

            actual.Should().AllSatisfy(value => value.Should().Be(expected));
        }

        /// <summary>
        /// 证明重新确认的读数在交易中被拒。
        /// </summary>
        /// <param name="grainStates">用于选择测试 grain的交易状态配置。</param>
        /// <returns>代表测试的任务。</returns>
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
