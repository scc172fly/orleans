using Orleans.Transactions.TestKit.xUnit;
using Xunit;

namespace Orleans.Transactions.Tests
{
    /// <summary>
    /// 测试使用内存存储时 Grain 发生故障情况下的事务行为。
    /// </summary>
    [TestCategory("BVT"), TestCategory("Transactions")]
    [TestSuite("BVT")]
    [TestProvider("None")]
    [TestArea("Transactions")]
    public class GrainFaultTransactionMemoryTests : GrainFaultTransactionTestRunnerxUnit, IClassFixture<MemoryTransactionsFixture>
    {
        public GrainFaultTransactionMemoryTests(MemoryTransactionsFixture fixture, ITestOutputHelper output)
            : base(fixture.GrainFactory, output)
        {
        }
    }
}
