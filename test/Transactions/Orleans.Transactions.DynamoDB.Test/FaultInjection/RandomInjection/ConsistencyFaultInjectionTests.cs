using Orleans.Transactions.TestKit.xUnit;
using Xunit;

namespace Orleans.Transactions.DynamoDB.Tests
{
    /// <summary>
    /// 使用 DynamoDB进行交易与随机错入一致的测试。
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
