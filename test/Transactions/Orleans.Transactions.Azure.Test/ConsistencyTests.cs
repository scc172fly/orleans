using Orleans.Transactions.TestKit.xUnit;
using Xunit;

namespace Orleans.Transactions.AzureStorage.Tests
{
    /// <summary>
    /// 测试使用 Azure Storage 时的事务一致性行为。
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
