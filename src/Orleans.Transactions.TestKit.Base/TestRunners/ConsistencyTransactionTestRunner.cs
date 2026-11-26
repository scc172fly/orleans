using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using AwesomeAssertions;
using Orleans.Transactions.TestKit.Consistency;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 随机处理交易工作量,核实其记录的历史是否一致。
    /// </summary>
    public abstract class ConsistencyTransactionTestRunner : TransactionTestRunnerBase
    {
        /// <summary>
        /// 初始化 <see cref="ConsistencyTransactionTestRunner"/> 类的新实例。
        /// </summary>
        /// <param name="grainFactory">下 grain 用于测试的工厂 grains.</param>
        /// <param name="output">用于写入测试输出的回调。</param>
        protected ConsistencyTransactionTestRunner(IGrainFactory grainFactory, Action<string> output)
        : base(grainFactory, output) { }


        // 依赖配置设置的配置设置会被运行中的子类覆盖
        // 允许测试根据特定环境调整逻辑或跳过
        /// <summary>
        /// 获得一个值,显示存储适配器是否有有限的空间进行记录。
        /// </summary>
        protected abstract bool StorageAdaptorHasLimitedCommitSpace { get; }

        /// <summary>
        /// 获得一个值,显示存储错误注入是否有效。
        /// </summary>
        protected abstract bool StorageErrorInjectionActive { get; }

        /// <summary>
        /// 运行一种确定性随机交易工作量,并核实由此产生的一致性历史。
        /// </summary>
        /// <param name="numGrains">编号数 grains 参与工作量。</param>
        /// <param name="scale">工作量表,它决定工人人数和交易。</param>
        /// <param name="avoidDeadlocks">工作量是否应当避免可能造成僵局的行动。</param>
        /// <param name="avoidTimeouts">工作量是否应当限制能够引入反应超时的行动。</param>
        /// <param name="readwrite">用于确定交易和 grain 电话是只读或只读写。</param>
        /// <returns>一项代表一致性测试的任务。</returns>
        [SuppressMessage("Security", "CA5394:Do not use insecure randomness",
            Justification = "A seeded PRNG derives workload seeds for transaction conformance tests.")]
        public virtual async Task RandomizedConsistency(int numGrains, int scale, bool avoidDeadlocks, bool avoidTimeouts, ReadWriteDetermination readwrite)
        {
            var random = new Random(scale + numGrains * 1000 + (avoidDeadlocks ? 666 : 333) + ((int)readwrite) * 123976);

            var harness = new ConsistencyTestHarness(grainFactory, numGrains, random.Next(), avoidDeadlocks, avoidTimeouts, readwrite, StorageErrorInjectionActive);

            // 首先,运行随机工作负荷以生成历史事件
            testOutput($"start at {DateTime.UtcNow}");
            int numThreads = scale;
            int numTxsPerThread = scale * scale;

            // 开始运行交易的线索
            var tasks = new Task[numThreads];
            for (int i = 0; i < numThreads; i++)
            {
                tasks[i] = harness.RunRandomTransactionSequence(i, numTxsPerThread, grainFactory, this.testOutput);
            }

            // 等待测试完成
            await Task.WhenAll(tasks);
            testOutput($"end at {DateTime.UtcNow}");

            // 金路:在避免僵局和锁定升级时,所有交易都有望通过
            if (!StorageErrorInjectionActive
                && avoidDeadlocks
                && (readwrite == ReadWriteDetermination.PerGrain || readwrite == ReadWriteDetermination.PerTransaction))
            {
                harness.NumAborted.Should().Be(0);
            }

            // 然后,分析历史结果
            var tolerateGenericTimeouts = ShouldTolerateGenericTimeouts(scale, StorageErrorInjectionActive);
            var tolerateUnknownExceptions = StorageAdaptorHasLimitedCommitSpace || StorageErrorInjectionActive;
            harness.CheckConsistency(tolerateGenericTimeouts, tolerateUnknownExceptions);
        }

        internal static bool ShouldTolerateGenericTimeouts(int scale, bool storageErrorInjectionActive)
        {
            // AvoidTimeouts 限制在答复超时前的重复工作,但无法阻止
            // 系统负荷时反应超时失效。
            return storageErrorInjectionActive || scale >= 3;
        }
    }
}
