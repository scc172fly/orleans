using Orleans.Transactions.TestKit.xUnit;
using Xunit;
using TestExtensions;

namespace Orleans.Transactions.Tests
{
    /// <summary>
    /// 测试禁用事务时的行为。
    /// </summary>
    [TestCategory("BVT"), TestCategory("Transactions")]
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Transactions")]
    public class DisabledTransactionsTests : DisabledTransactionsTestRunnerxUnit, IClassFixture<DefaultClusterFixture>
    {
        public DisabledTransactionsTests(DefaultClusterFixture fixture, ITestOutputHelper output)
            : base(fixture.GrainFactory, output)
        {
        }
    }
}
