
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 运行交易时承诺在实施过程中失败或丢弃的服务情景。
    /// </summary>
    public abstract class TocFaultTransactionTestRunner : TransactionTestRunnerBase
    {
        private static readonly TimeSpan VerificationRetryTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan VerificationRetryDelay = TimeSpan.FromMilliseconds(100);

        /// <summary>
        /// 初始化 <see cref="TocFaultTransactionTestRunner"/> 类的新实例。
        /// </summary>
        /// <param name="grainFactory">下 grain 用于测试的工厂 grains.</param>
        /// <param name="output">用于写入测试输出的回调。</param>
        protected TocFaultTransactionTestRunner(IGrainFactory grainFactory, Action<string> output)
        : base(grainFactory, output) { }

        /// <summary>
        /// 证明一次承诺服务中断会中止多grain 交易并保留先前承诺状态。
        /// </summary>
        /// <param name="grainStates">用于选择测试 grains的交易状态配置。</param>
        /// <param name="grainCount">编号数 grains 参与交易。</param>
        /// <returns>代表测试的任务。</returns>
        public virtual async Task MultiGrainWriteTransactionWithCommitFailure(string grainStates, int grainCount)
        {
            const int expected = 5;

            ITransactionCommitterTestGrain committer = this.grainFactory.GetGrain<ITransactionCommitterTestGrain>(Guid.NewGuid());
            List<ITransactionTestGrain> grains =
                Enumerable.Range(0, grainCount)
                    .Select(i => RandomTestGrain(grainStates))
                    .ToList();

            ITransactionCoordinatorGrain coordinator = this.grainFactory.GetGrain<ITransactionCoordinatorGrain>(Guid.NewGuid());

            await coordinator.MultiGrainAdd(committer, new PassOperation("pass"), grains, expected);

            Func<Task> task = () => coordinator.MultiGrainAdd(committer, new FailOperation("fail"), grains, expected);
            await task.Should().ThrowAsync<OrleansTransactionAbortedException>();

            foreach (var grain in grains)
            {
                var actualValues = await GetWithTransientFailureRetry(grain);
                foreach (var actual in actualValues)
                {
                    actual.Should().Be(expected);
                }
            }

            // TODO:添加进行服务确认的核查,接收有适当参数的呼叫。
        }

        /// <summary>
        /// 证明承诺服务例外情况报告为怀疑的结果,同时保留先前承诺状态。
        /// </summary>
        /// <param name="grainStates">用于选择测试 grains的交易状态配置。</param>
        /// <param name="grainCount">编号数 grains 参与交易。</param>
        /// <returns>代表测试的任务。</returns>
        public virtual async Task MultiGrainWriteTransactionWithCommitException(string grainStates, int grainCount)
        {
            const int expected = 5;

            ITransactionCommitterTestGrain committer = this.grainFactory.GetGrain<ITransactionCommitterTestGrain>(Guid.NewGuid());
            List<ITransactionTestGrain> grains =
                Enumerable.Range(0, grainCount)
                    .Select(i => RandomTestGrain(grainStates))
                    .ToList();

            ITransactionCoordinatorGrain coordinator = this.grainFactory.GetGrain<ITransactionCoordinatorGrain>(Guid.NewGuid());

            await coordinator.MultiGrainAdd(committer, new PassOperation("pass"), grains, expected);

            Func<Task> task = () => coordinator.MultiGrainAdd(committer, new ThrowOperation("throw"), grains, expected);
            await task.Should().ThrowAsync<OrleansTransactionInDoubtException>();

            foreach (var grain in grains)
            {
                var actualValues = await GetWithTransientFailureRetry(grain);
                foreach (var actual in actualValues)
                {
                    actual.Should().Be(expected);
                }
            }

            // TODO:添加进行服务确认的核查,接收有适当参数的呼叫。
        }

        private async Task<int[]> GetWithTransientFailureRetry(ITransactionTestGrain grain)
        {
            var retryStart = Stopwatch.GetTimestamp();

            while (true)
            {
                try
                {
                    return await grain.Get();
                }
                // 承诺结果可在注销通知每个参与者之前到达。
                // 重叠的清理被故意中止,且可以安全重试。
                catch (OrleansTransactionTransientFailureException exception)
                    when (Stopwatch.GetElapsedTime(retryStart) < VerificationRetryTimeout)
                {
                    this.testOutput($"State verification failed transiently: {exception.Message}. Retrying.");
                    await Task.Delay(VerificationRetryDelay);
                }
            }
        }
    }
}
