using System.Threading.Tasks;
using Orleans.Transactions.TestKit.Consistency;
using Xunit;

namespace Orleans.Transactions.TestKit.xUnit
{
    /// <inheritdoc cref="ConsistencyTransactionTestRunner"/>
    public abstract class ConsistencyTransactionTestRunnerxUnit : ConsistencyTransactionTestRunner
    {
        /// <summary>
        /// 初始化 <see cref="ConsistencyTransactionTestRunnerxUnit"/> 类的新实例。
        /// </summary>
        /// <param name="grainFactory">用于访问测试 Grain 的 Grain 工厂。</param>
        /// <param name="output">xUnit 测试输出助手。</param>
        public ConsistencyTransactionTestRunnerxUnit(IGrainFactory grainFactory, ITestOutputHelper output)
            : base(grainFactory, TestOutputHelperExtensions.GetWriteLine(output, nameof(output)))
        {
        }

        /// <inheritdoc cref="ConsistencyTransactionTestRunner.StorageAdaptorHasLimitedCommitSpace"/>
        protected override bool StorageAdaptorHasLimitedCommitSpace => true;

        /// <inheritdoc cref="ConsistencyTransactionTestRunner.StorageErrorInjectionActive"/>
        protected override bool StorageErrorInjectionActive => true;

        /// <inheritdoc cref="ConsistencyTransactionTestRunner.RandomizedConsistency(int, int, bool, bool, ReadWriteDetermination)"/>
        [Theory]
        // 高争用
        [InlineData(2, 2, true, true, ReadWriteDetermination.PerGrain)]
        [InlineData(2, 3, true, true, ReadWriteDetermination.PerGrain)]
        [InlineData(2, 4, true, true, ReadWriteDetermination.PerGrain)]
        [InlineData(2, 5, true, true, ReadWriteDetermination.PerGrain)]
        [InlineData(2, 2, true, true, ReadWriteDetermination.PerTransaction)]
        [InlineData(2, 3, true, true, ReadWriteDetermination.PerTransaction)]
        [InlineData(2, 4, true, true, ReadWriteDetermination.PerTransaction)]
        [InlineData(2, 5, true, true, ReadWriteDetermination.PerTransaction)]
        [InlineData(2, 2, true, true, ReadWriteDetermination.PerAccess)]
        [InlineData(2, 3, true, true, ReadWriteDetermination.PerAccess)]
        [InlineData(2, 4, true, true, ReadWriteDetermination.PerAccess)]
        [InlineData(2, 5, true, true, ReadWriteDetermination.PerAccess)]
        [InlineData(2, 2, false, true, ReadWriteDetermination.PerAccess)]
        [InlineData(2, 3, false, true, ReadWriteDetermination.PerAccess)]
        [InlineData(2, 4, false, true, ReadWriteDetermination.PerAccess)]
        [InlineData(2, 5, false, true, ReadWriteDetermination.PerAccess)]
        [InlineData(2, 2, true, false, ReadWriteDetermination.PerGrain)]
        [InlineData(2, 3, true, false, ReadWriteDetermination.PerGrain)]
        [InlineData(2, 4, true, false, ReadWriteDetermination.PerGrain)]
        [InlineData(2, 5, true, false, ReadWriteDetermination.PerGrain)]
        [InlineData(2, 2, true, false, ReadWriteDetermination.PerTransaction)]
        [InlineData(2, 3, true, false, ReadWriteDetermination.PerTransaction)]
        [InlineData(2, 4, true, false, ReadWriteDetermination.PerTransaction)]
        [InlineData(2, 5, true, false, ReadWriteDetermination.PerTransaction)]
        [InlineData(2, 2, true, false, ReadWriteDetermination.PerAccess)]
        [InlineData(2, 3, true, false, ReadWriteDetermination.PerAccess)]
        [InlineData(2, 4, true, false, ReadWriteDetermination.PerAccess)]
        [InlineData(2, 5, true, false, ReadWriteDetermination.PerAccess)]
        [InlineData(2, 2, false, false, ReadWriteDetermination.PerAccess)]
        [InlineData(2, 3, false, false, ReadWriteDetermination.PerAccess)]
        [InlineData(2, 4, false, false, ReadWriteDetermination.PerAccess)]
        [InlineData(2, 5, false, false, ReadWriteDetermination.PerAccess)]
        // 中等争用
        [InlineData(30, 2, true, true, ReadWriteDetermination.PerGrain)]
        [InlineData(30, 3, true, true, ReadWriteDetermination.PerGrain)]
        [InlineData(30, 4, true, true, ReadWriteDetermination.PerGrain)]
        [InlineData(30, 2, true, true, ReadWriteDetermination.PerTransaction)]
        [InlineData(30, 3, true, true, ReadWriteDetermination.PerTransaction)]
        [InlineData(30, 4, true, true, ReadWriteDetermination.PerTransaction)]
        [InlineData(30, 2, true, true, ReadWriteDetermination.PerAccess)]
        [InlineData(30, 3, true, true, ReadWriteDetermination.PerAccess)]
        [InlineData(30, 4, true, true, ReadWriteDetermination.PerAccess)]
        [InlineData(30, 2, false, true, ReadWriteDetermination.PerAccess)]
        [InlineData(30, 3, false, true, ReadWriteDetermination.PerAccess)]
        [InlineData(30, 4, false, true, ReadWriteDetermination.PerAccess)]
        [InlineData(30, 2, true, false, ReadWriteDetermination.PerGrain)]
        [InlineData(30, 3, true, false, ReadWriteDetermination.PerGrain)]
        [InlineData(30, 4, true, false, ReadWriteDetermination.PerGrain)]
        [InlineData(30, 5, true, false, ReadWriteDetermination.PerGrain)]
        [InlineData(30, 2, true, false, ReadWriteDetermination.PerTransaction)]
        [InlineData(30, 3, true, false, ReadWriteDetermination.PerTransaction)]
        [InlineData(30, 4, true, false, ReadWriteDetermination.PerTransaction)]
        [InlineData(30, 5, true, false, ReadWriteDetermination.PerTransaction)]
        [InlineData(30, 2, true, false, ReadWriteDetermination.PerAccess)]
        [InlineData(30, 3, true, false, ReadWriteDetermination.PerAccess)]
        [InlineData(30, 4, true, false, ReadWriteDetermination.PerAccess)]
        [InlineData(30, 5, true, false, ReadWriteDetermination.PerAccess)]
        [InlineData(30, 2, false, false, ReadWriteDetermination.PerAccess)]
        [InlineData(30, 3, false, false, ReadWriteDetermination.PerAccess)]
        [InlineData(30, 4, false, false, ReadWriteDetermination.PerAccess)]
        [InlineData(30, 5, false, false, ReadWriteDetermination.PerAccess)]
        // 低争用
        [InlineData(1000, 2, false, true, ReadWriteDetermination.PerGrain)]
        [InlineData(1000, 3, false, true, ReadWriteDetermination.PerGrain)]
        [InlineData(1000, 4, false, true, ReadWriteDetermination.PerGrain)]
        [InlineData(1000, 2, false, true, ReadWriteDetermination.PerTransaction)]
        [InlineData(1000, 3, false, true, ReadWriteDetermination.PerTransaction)]
        [InlineData(1000, 4, false, true, ReadWriteDetermination.PerTransaction)]
        [InlineData(1000, 2, false, true, ReadWriteDetermination.PerAccess)]
        [InlineData(1000, 3, false, true, ReadWriteDetermination.PerAccess)]
        [InlineData(1000, 4, false, true, ReadWriteDetermination.PerAccess)]
        [InlineData(1000, 2, false, false, ReadWriteDetermination.PerGrain)]
        [InlineData(1000, 3, false, false, ReadWriteDetermination.PerGrain)]
        [InlineData(1000, 4, false, false, ReadWriteDetermination.PerGrain)]
        [InlineData(1000, 5, false, false, ReadWriteDetermination.PerGrain)]
        [InlineData(1000, 2, false, false, ReadWriteDetermination.PerTransaction)]
        [InlineData(1000, 3, false, false, ReadWriteDetermination.PerTransaction)]
        [InlineData(1000, 4, false, false, ReadWriteDetermination.PerTransaction)]
        [InlineData(1000, 5, false, false, ReadWriteDetermination.PerTransaction)]
        [InlineData(1000, 2, false, false, ReadWriteDetermination.PerAccess)]
        [InlineData(1000, 3, false, false, ReadWriteDetermination.PerAccess)]
        [InlineData(1000, 4, false, false, ReadWriteDetermination.PerAccess)]
        [InlineData(1000, 5, false, false, ReadWriteDetermination.PerAccess)]
        public override Task RandomizedConsistency(int numGrains, int scale, bool avoidDeadlocks,
            bool avoidTimeouts, ReadWriteDetermination readwrite)
        {
            return base.RandomizedConsistency(numGrains, scale, avoidDeadlocks, avoidTimeouts, readwrite);
        }
    }
}
