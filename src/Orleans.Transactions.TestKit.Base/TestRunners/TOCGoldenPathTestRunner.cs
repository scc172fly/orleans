
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 执行正常的事务提交服务场景。
    /// </summary>
    public abstract class TocGoldenPathTestRunner : TransactionTestRunnerBase
    {
        /// <summary>
        /// 初始化 <see cref="TocGoldenPathTestRunner"/> 类的新实例。
        /// </summary>
        /// <param name="grainFactory">用于访问测试 Grain 的 Grain 工厂。</param>
        /// <param name="output">用于写入测试输出的回调。</param>
        protected TocGoldenPathTestRunner(IGrainFactory grainFactory, Action<string> output)
        : base(grainFactory, output) { }

        /// <summary>
        /// 验证与事务提交服务协同的多 Grain 写入成功。
        /// </summary>
        /// <param name="grainStates">用于选择测试 Grain 的事务状态配置。</param>
        /// <param name="grainCount">参与事务的 Grain 数量。</param>
        /// <returns>表示该测试的任务。</returns>
        public virtual async Task MultiGrainWriteTransaction(string grainStates, int grainCount)
        {
            const int expected = 5;

            ITransactionCommitterTestGrain committer = this.grainFactory.GetGrain<ITransactionCommitterTestGrain>(Guid.NewGuid());
            List<ITransactionTestGrain> grains =
                Enumerable.Range(0, grainCount)
                    .Select(i => RandomTestGrain(grainStates))
                    .ToList();

            ITransactionCoordinatorGrain coordinator = this.grainFactory.GetGrain<ITransactionCoordinatorGrain>(Guid.NewGuid());

            await coordinator.MultiGrainAdd(committer, new PassOperation("pass"), grains, expected);

            foreach (var grain in grains)
            {
                var actualValues = await grain.Get();
                foreach (var actual in actualValues)
                {
                    actual.Should().Be(expected);
                }
            }

            // TODO：增加验证，确保提交服务收到具有正确参数的调用。
        }
    }
}
