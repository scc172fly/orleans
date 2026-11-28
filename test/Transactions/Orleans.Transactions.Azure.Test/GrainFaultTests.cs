using Orleans.Transactions.TestKit.xUnit;
using Xunit;

namespace Orleans.Transactions.AzureStorage.Tests
{
    /// <summary>
    /// 测试使用 Azure Storage 时 Grain 发生故障情况下的事务行为。
    /// </summary>
    [TestCategory("AzureStorage"), TestCategory("Transactions"), TestCategory("Functional")]
    [TestSuite("Functional")]
    [TestProvider("AzureStorage")]
    [TestArea("Transactions")]
    public class GrainFaultTests : GrainFaultTransactionTestRunnerxUnit, IClassFixture<TestFixture>
    {
        public GrainFaultTests(TestFixture fixture, ITestOutputHelper output)
            : base(fixture.GrainFactory, output)
        {
            fixture.EnsurePreconditionsMet();
        }
    }
}
