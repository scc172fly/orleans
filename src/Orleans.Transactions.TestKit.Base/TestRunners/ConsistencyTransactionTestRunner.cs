using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using AwesomeAssertions;
using Orleans.Transactions.TestKit.Consistency;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 执行随机事务工作负载并验证其记录的历史是否一致。
    /// </summary>
    public abstract class ConsistencyTransactionTestRunner : TransactionTestRunnerBase
    {
        /// <summary>
        /// 初始化 <see cref="ConsistencyTransactionTestRunner"/> 类的新实例。
        /// </summary>
        /// <param name="grainFactory">用于访问测试 Grain 的 Grain 工厂。</param>
        /// <param name="output">用于写入测试输出的回调。</param>
        protected ConsistencyTransactionTestRunner(IGrainFactory grainFactory, Action<string> output)
        : base(grainFactory, output) { }


        // 依赖配置的设置可由运行器子类重写
        // 这样测试可以针对特定上下文调整逻辑或被跳过
        /// <summary>
        /// 获取一个值，指示存储适配器用于保存提交记录的空间是否有限。
        /// </summary>
        protected abstract bool StorageAdaptorHasLimitedCommitSpace { get; }

        /// <summary>
        /// 获取一个值，指示存储错误注入是否已启用。
        /// </summary>
        protected abstract bool StorageErrorInjectionActive { get; }

        /// <summary>
        /// 执行确定性的随机事务工作负载，并验证所得历史的一致性。
        /// </summary>
        /// <param name="numGrains">参与工作负载的 Grain 数量。</param>
        /// <param name="scale">工作负载规模，决定工作线程和事务的数量。</param>
        /// <param name="avoidDeadlocks">工作负载是否应避免可能导致死锁的操作。</param>
        /// <param name="avoidTimeouts">工作负载是否应限制可能导致响应超时的操作。</param>
        /// <param name="readwrite">确定事务和 Grain 调用是只读还是读写的策略。</param>
        /// <returns>表示一致性测试的任务。</returns>
        [SuppressMessage("Security", "CA5394:Do not use insecure randomness",
            Justification = "A seeded PRNG derives workload seeds for transaction conformance tests.")]
        public virtual async Task RandomizedConsistency(int numGrains, int scale, bool avoidDeadlocks, bool avoidTimeouts, ReadWriteDetermination readwrite)
        {
            var random = new Random(scale + numGrains * 1000 + (avoidDeadlocks ? 666 : 333) + ((int)readwrite) * 123976);

            var harness = new ConsistencyTestHarness(grainFactory, numGrains, random.Next(), avoidDeadlocks, avoidTimeouts, readwrite, StorageErrorInjectionActive);

            // 首先执行随机工作负载以生成历史事件
            testOutput($"start at {DateTime.UtcNow}");
            int numThreads = scale;
            int numTxsPerThread = scale * scale;

            // 启动执行事务的线程
            var tasks = new Task[numThreads];
            for (int i = 0; i < numThreads; i++)
            {
                tasks[i] = harness.RunRandomTransactionSequence(i, numTxsPerThread, grainFactory, this.testOutput);
            }

            // 等待测试完成
            await Task.WhenAll(tasks);
            testOutput($"end at {DateTime.UtcNow}");

            // 正常路径：在避免死锁和锁升级的情况下，预期所有事务均成功
            if (!StorageErrorInjectionActive
                && avoidDeadlocks
                && (readwrite == ReadWriteDetermination.PerGrain || readwrite == ReadWriteDetermination.PerTransaction))
            {
                harness.NumAborted.Should().Be(0);
            }

            // 然后分析历史结果
            var tolerateGenericTimeouts = ShouldTolerateGenericTimeouts(scale, StorageErrorInjectionActive);
            var tolerateUnknownExceptions = StorageAdaptorHasLimitedCommitSpace || StorageErrorInjectionActive;
            harness.CheckConsistency(tolerateGenericTimeouts, tolerateUnknownExceptions);
        }

        internal static bool ShouldTolerateGenericTimeouts(int scale, bool storageErrorInjectionActive)
        {
            // AvoidTimeouts 会在响应超时前限制递归工作，但当系统处于负载下时，无法阻止
            // 响应超时本身到期。
            return storageErrorInjectionActive || scale >= 3;
        }
    }
}
