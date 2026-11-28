using Orleans.Transactions.TestKit.xUnit;
using Xunit;

namespace Orleans.Transactions.DynamoDB.Tests
{
    /// <summary>
    /// 测试使用 DynamoDB 时 Grain 发生故障情况下的事务行为。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("DynamoDB")]
    [TestArea("Transactions")]
    [TestCategory("DynamoDB"), TestCategory("Transactions"), TestCategory("Functional")]
    public class GrainFaultTests : GrainFaultTransactionTestRunnerxUnit, IClassFixture<TestFixture>
    {
        public GrainFaultTests(TestFixture fixture, ITestOutputHelper output)
            : base(fixture.GrainFactory, output)
        {
            fixture.EnsurePreconditionsMet();
        }
    }
}
