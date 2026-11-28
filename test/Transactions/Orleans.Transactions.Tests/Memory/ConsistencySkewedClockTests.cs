using Orleans.Transactions.TestKit.xUnit;
using Xunit;

namespace Orleans.Transactions.Tests
{
    /// <summary>
    /// 测试使用内存存储时存在时钟偏差场景下的事务一致性。
    /// </summary>
    [TestCategory("Transactions-dev")]
    [TestSuite("Nightly")]
    [TestProvider("None")]
    [TestArea("Transactions")]
    public class ConsistencySkewedClockTests : ConsistencyTransactionTestRunnerxUnit, IClassFixture<SkewedClockMemoryTransactionsFixture>
    {
        public ConsistencySkewedClockTests(SkewedClockMemoryTransactionsFixture fixture, ITestOutputHelper output)
            : base(fixture.GrainFactory, output)
        {
        }

        protected override bool StorageErrorInjectionActive => false;
        protected override bool StorageAdaptorHasLimitedCommitSpace => false;

    }
}
