using Microsoft.Extensions.DependencyInjection;

using Orleans.Transactions.TestKit.xUnit;

using Xunit;

namespace Orleans.Transactions.AzureStorage.Tests
{
    /// <summary>
    /// 用于测试 Azure 存储的“范围交易功能”测试。
    /// </summary>
    [TestCategory("AzureStorage"), TestCategory("Transactions"), TestCategory("Functional")]
    [TestSuite("Functional")]
    [TestProvider("AzureStorage")]
    [TestArea("Transactions")]
    public class TransactionScopeTests : ScopedTransactionsTestRunnerxUnit, IClassFixture<TestFixture>
    {
        public TransactionScopeTests(TestFixture fixture, ITestOutputHelper output)
            : base(fixture.GrainFactory, fixture.HostedCluster.ServiceProvider.GetRequiredService<ITransactionClient>(), output)
        {
            fixture.EnsurePreconditionsMet();
        }
    }
}
