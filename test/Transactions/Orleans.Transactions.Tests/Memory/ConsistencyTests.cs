using Orleans.Transactions.TestKit.xUnit;
using Xunit;

namespace Orleans.Transactions.Tests
{
    /// <summary>
    /// 测试使用内存存储时的事务一致性行为。
    /// </summary>
    [TestCategory("Transactions-dev")]
    [TestSuite("Nightly")]
    [TestProvider("None")]
    [TestArea("Transactions")]
    public class ConsistencyTests : ConsistencyTransactionTestRunnerxUnit, IClassFixture<MemoryTransactionsFixture>
    {
        public ConsistencyTests(MemoryTransactionsFixture fixture, ITestOutputHelper output)
            : base(fixture.GrainFactory, output)
        {
        }

        protected override bool StorageErrorInjectionActive => false;
        protected override bool StorageAdaptorHasLimitedCommitSpace => false;

    }

}
