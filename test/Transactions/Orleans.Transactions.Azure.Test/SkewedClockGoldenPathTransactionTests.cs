using Orleans.Transactions.TestKit.xUnit;
using Xunit;

namespace Orleans.Transactions.AzureStorage.Tests
{
    /// <summary>
    /// 测试使用 Azure Storage 时存在时钟偏差场景下的事务正常成功路径。
    /// </summary>
    [TestCategory("AzureStorage"), TestCategory("Transactions"), TestCategory("Functional")]
    [TestSuite("Functional")]
    [TestProvider("AzureStorage")]
    [TestArea("Transactions")]
    public class SkewedClockGoldenPathTransactionTests : GoldenPathTransactionTestRunnerxUnit, IClassFixture<SkewedClockTestFixture>
    {
        public SkewedClockGoldenPathTransactionTests(SkewedClockTestFixture fixture, ITestOutputHelper output)
            : base(fixture.GrainFactory, output)
        {
            fixture.EnsurePreconditionsMet();
        }

    }
}
