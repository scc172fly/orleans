using Orleans.Transactions.TestKit.xUnit;
using Xunit;

namespace Orleans.Transactions.AzureStorage.Tests
{
    /// <summary>
    /// 测试使用 Azure Storage 时随机故障注入场景下的事务一致性。
    /// </summary>
    [TestCategory("AzureStorage"), TestCategory("Transactions-dev")]
    [TestSuite("Nightly")]
    [TestProvider("AzureStorage")]
    [TestArea("Transactions")]
    public class ConsistencyFaultInjectionTests : ConsistencyTransactionTestRunnerxUnit, IClassFixture<RandomFaultInjectedTestFixture>
    {
        public ConsistencyFaultInjectionTests(RandomFaultInjectedTestFixture fixture, ITestOutputHelper output)
            : base(fixture.GrainFactory, output)
        { }

        protected override bool StorageAdaptorHasLimitedCommitSpace => true;
        protected override bool StorageErrorInjectionActive => true;
    }
}
