using Orleans.Transactions.TestKit.xUnit;
using Xunit;

namespace Orleans.Transactions.AzureStorage.Tests
{
    /// <summary>
    /// 与 Azure 存储进行交易一致性行为测试。
    /// </summary>
    [TestCategory("AzureStorage"), TestCategory("Transactions-dev")]
    [TestSuite("Nightly")]
    [TestProvider("AzureStorage")]
    [TestArea("Transactions")]
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
