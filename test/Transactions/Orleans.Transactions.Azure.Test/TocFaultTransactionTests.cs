
using Orleans.Transactions.TestKit.xUnit;
using Xunit;

namespace Orleans.Transactions.AzureStorage.Tests
{
    /// <summary>
    /// 测试使用 Azure Storage 时协调权转移（TOC）的故障场景。
    /// </summary>
    [TestCategory("AzureStorage"), TestCategory("Transactions"), TestCategory("Functional")]
    [TestSuite("Functional")]
    [TestProvider("AzureStorage")]
    [TestArea("Transactions")]
    public class TocFaultTransactionTests : TocFaultTransactionTestRunnerxUnit, IClassFixture<TestFixture>
    {
        public TocFaultTransactionTests(TestFixture fixture, ITestOutputHelper output)
            : base(fixture.GrainFactory, output)
        {
            fixture.EnsurePreconditionsMet();
        }
    }
}
