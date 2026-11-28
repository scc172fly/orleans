using Orleans.Transactions.TestKit.xUnit;
using Xunit;

namespace Orleans.Transactions.DynamoDB.Tests
{
    /// <summary>
    /// 测试使用 DynamoDB 时的事务一致性行为。
    /// </summary>
    [TestSuite("Nightly")]
    [TestArea("Transactions")]
    [TestProvider("DynamoDB")]
    [TestCategory("DynamoDB"), TestCategory("Transactions-dev")]
    public class ConsistencyTests : ConsistencyTransactionTestRunnerxUnit, IClassFixture<TestFixture>
    {
        public ConsistencyTests(TestFixture fixture, ITestOutputHelper output)
            : base(fixture.GrainFactory, output)
        {
        }

        protected override bool StorageAdaptorHasLimitedCommitSpace => true;
        protected override bool StorageErrorInjectionActive => false;
    }
}
