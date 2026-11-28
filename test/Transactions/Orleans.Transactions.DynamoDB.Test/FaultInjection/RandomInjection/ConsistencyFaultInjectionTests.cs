using Orleans.Transactions.TestKit.xUnit;
using Xunit;

namespace Orleans.Transactions.DynamoDB.Tests
{
    /// <summary>
    /// 测试使用 DynamoDB 时随机故障注入场景下的事务一致性。
    /// </summary>
    [TestSuite("Nightly")]
    [TestArea("Transactions")]
    [TestProvider("DynamoDB")]
    [TestCategory("DynamoDB"), TestCategory("Transactions-dev")]
    public class ConsistencyFaultInjectionTests : ConsistencyTransactionTestRunnerxUnit, IClassFixture<RandomFaultInjectedTestFixture>
    {
        public ConsistencyFaultInjectionTests(RandomFaultInjectedTestFixture fixture, ITestOutputHelper output)
            : base(fixture.GrainFactory, output)
        { }

        protected override bool StorageAdaptorHasLimitedCommitSpace => true;
        protected override bool StorageErrorInjectionActive => true;
    }
}
