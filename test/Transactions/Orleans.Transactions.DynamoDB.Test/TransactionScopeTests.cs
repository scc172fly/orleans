using Microsoft.Extensions.DependencyInjection;

using Orleans.Transactions.TestKit.xUnit;

using Xunit;

namespace Orleans.Transactions.DynamoDB.Tests
{
    /// <summary>
    /// 测试使用 DynamoDB 时的事务作用域功能。
    /// </summary>
    [TestSuite("Functional")]
    [TestProvider("DynamoDB")]
    [TestArea("Transactions")]
    [TestCategory("DynamoDB"), TestCategory("Transactions"), TestCategory("Functional")]
    public class TransactionScopeTests : ScopedTransactionsTestRunnerxUnit, IClassFixture<TestFixture>
    {
        public TransactionScopeTests(TestFixture fixture, ITestOutputHelper output)
            : base(fixture.GrainFactory, fixture.HostedCluster.ServiceProvider.GetRequiredService<ITransactionClient>(), output)
        {
            fixture.EnsurePreconditionsMet();
        }
    }
}
