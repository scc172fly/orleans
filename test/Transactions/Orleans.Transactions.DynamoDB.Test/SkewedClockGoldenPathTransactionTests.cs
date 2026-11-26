using Orleans.Transactions.TestKit.xUnit;
using Xunit;

namespace Orleans.Transactions.DynamoDB.Tests
{
    /// <summary>
    /// 使用 DynamoDB的斜时钟测试交易金色路径假想。
    /// </summary>
    [TestCategory("DynamoDB"), TestCategory("Transactions"), TestCategory("Functional")]
    [TestSuite("Functional")]
    [TestProvider("DynamoDB")]
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
