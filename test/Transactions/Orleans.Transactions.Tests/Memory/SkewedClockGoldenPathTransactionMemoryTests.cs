using Orleans.Transactions.TestKit.xUnit;
using Xunit;

namespace Orleans.Transactions.Tests
{
    /// <summary>
    /// 测试使用内存存储时存在时钟偏差场景下的事务正常成功路径。
    /// </summary>
    [TestCategory("BVT"), TestCategory("Transactions")]
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Transactions")]
    public class SkewedClockGoldenPathTransactionMemoryTests : GoldenPathTransactionTestRunnerxUnit, IClassFixture<SkewedClockMemoryTransactionsFixture>
    {
        public SkewedClockGoldenPathTransactionMemoryTests(SkewedClockMemoryTransactionsFixture fixture, ITestOutputHelper output)
            : base(fixture.GrainFactory, output)
        {
        }
    }
}
