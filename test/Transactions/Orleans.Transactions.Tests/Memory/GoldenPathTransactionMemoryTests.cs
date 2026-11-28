using Orleans.Transactions.TestKit.xUnit;
using Xunit;

namespace Orleans.Transactions.Tests
{
    /// <summary>
    /// 测试使用内存存储时事务正常成功路径的场景。
    /// </summary>
    [TestCategory("BVT"), TestCategory("Transactions")]
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Transactions")]
    public class GoldenPathTransactionMemoryTests : GoldenPathTransactionTestRunnerxUnit, IClassFixture<MemoryTransactionsFixture>
    {
        public GoldenPathTransactionMemoryTests(MemoryTransactionsFixture fixture, ITestOutputHelper output)
            : base(fixture.GrainFactory, output)
        {
        }
    }
}
