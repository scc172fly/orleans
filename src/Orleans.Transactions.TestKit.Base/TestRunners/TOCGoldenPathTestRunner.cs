
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 运行成功的交易承诺服务情景。
    /// </summary>
    public abstract class TocGoldenPathTestRunner : TransactionTestRunnerBase
    {
        /// <summary>
        /// 初始化 <see cref="TocGoldenPathTestRunner"/> 类的新实例。
        /// </summary>
        /// <param name="grainFactory">下 grain 用于测试的工厂 grains.</param>
        /// <param name="output">用于写入测试输出的回调。</param>
        protected TocGoldenPathTestRunner(IGrainFactory grainFactory, Action<string> output)
        : base(grainFactory, output) { }

        /// <summary>
        /// 验证成功多grain 写作,并与交易承诺服务协调。
        /// </summary>
        /// <param name="grainStates">用于选择测试 grains的交易状态配置。</param>
        /// <param name="grainCount">编号数 grains 参与交易。</param>
        /// <returns>代表测试的任务。</returns>
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

            // TODO:添加进行服务确认的核查,接收有适当参数的呼叫。
        }
    }
}
