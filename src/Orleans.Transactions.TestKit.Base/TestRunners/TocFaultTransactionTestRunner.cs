
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AwesomeAssertions;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 执行提交服务在提交时失败或引发异常的事务场景。
    /// </summary>
    public abstract class TocFaultTransactionTestRunner : TransactionTestRunnerBase
    {
        private static readonly TimeSpan VerificationRetryTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan VerificationRetryDelay = TimeSpan.FromMilliseconds(100);

        /// <summary>
        /// 初始化 <see cref="TocFaultTransactionTestRunner"/> 类的新实例。
        /// </summary>
        /// <param name="grainFactory">用于访问测试 Grain 的 Grain 工厂。</param>
        /// <param name="output">用于写入测试输出的回调。</param>
        protected TocFaultTransactionTestRunner(IGrainFactory grainFactory, Action<string> output)
        : base(grainFactory, output) { }

        /// <summary>
        /// 验证提交服务失败会中止多 Grain 事务并保留先前的已提交状态。
        /// </summary>
        /// <param name="grainStates">用于选择测试 Grain 的事务状态配置。</param>
        /// <param name="grainCount">参与事务的 Grain 数量。</param>
        /// <returns>表示该测试的任务。</returns>
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

            // TODO：增加验证，确保提交服务收到具有正确参数的调用。
        }

        /// <summary>
        /// 验证提交服务异常会报告结果不确定，并保留先前的已提交状态。
        /// </summary>
        /// <param name="grainStates">用于选择测试 Grain 的事务状态配置。</param>
        /// <param name="grainCount">参与事务的 Grain 数量。</param>
        /// <returns>表示该测试的任务。</returns>
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

            // TODO：增加验证，确保提交服务收到具有正确参数的调用。
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
                // 提交结果可能在取消操作到达所有参与者之前就已返回。若验证性读取
                // 与该清理过程重叠，则会被有意中止，并且可以安全重试。
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
