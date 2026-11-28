using Orleans.Transactions.TestKit.xUnit;
using Xunit;

namespace Orleans.Transactions.DynamoDB.Tests
{
    /// <summary>
    /// 测试使用 DynamoDB 时的事务并发行为。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("DynamoDB")]
    [TestArea("Transactions")]
    [TestCategory("DynamoDB"), TestCategory("Transactions"), TestCategory("Functional")]
    public class TransactionConcurrencyTests : TransactionConcurrencyTestRunnerxUnit, IClassFixture<TestFixture>
    {
        public TransactionConcurrencyTests(TestFixture fixture, ITestOutputHelper output)
            : base(fixture.GrainFactory, output)
        {
            fixture.EnsurePreconditionsMet();
        }
    }
}
