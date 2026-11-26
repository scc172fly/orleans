using Orleans.Transactions.TestKit.xUnit;
using Xunit;

namespace Orleans.Transactions.AzureStorage.Tests
{
    /// <summary>
    /// 使用 Azure 存储,测试交易是否与倾斜时钟情景相一致。
    /// </summary>
    [TestCategory("AzureStorage"), TestCategory("Transactions-dev")]
    [TestSuite("Nightly")]
    [TestProvider("AzureStorage")]
    [TestArea("Transactions")]
    public class ConsistencySkewedClockTests : ConsistencyTransactionTestRunnerxUnit, IClassFixture<SkewedClockTestFixture>
    {
        public ConsistencySkewedClockTests(SkewedClockTestFixture fixture, ITestOutputHelper output)
            : base(fixture.GrainFactory, output)
        {
        }

        protected override bool StorageAdaptorHasLimitedCommitSpace => true;
        protected override bool StorageErrorInjectionActive => false;
    }
}
