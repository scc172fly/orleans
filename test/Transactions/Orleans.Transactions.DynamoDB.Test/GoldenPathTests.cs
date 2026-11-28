using Orleans.Transactions.TestKit.xUnit;
using Xunit;

namespace Orleans.Transactions.DynamoDB.Tests
{
    /// <summary>
    /// 测试使用 DynamoDB 时事务正常成功路径的场景。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("DynamoDB")]
    [TestArea("Transactions")]
    [TestCategory("DynamoDB"), TestCategory("Transactions"), TestCategory("Functional")]
    public class GoldenPathTests : GoldenPathTransactionTestRunnerxUnit, IClassFixture<TestFixture>
    {
        public GoldenPathTests(TestFixture fixture, ITestOutputHelper output)
            : base(fixture.GrainFactory, output)
        {
            fixture.EnsurePreconditionsMet();
        }
    }
}
