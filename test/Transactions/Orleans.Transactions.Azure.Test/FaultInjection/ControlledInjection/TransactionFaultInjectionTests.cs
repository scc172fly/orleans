using Orleans.Transactions.AzureStorage.Tests;
using Orleans.Transactions.TestKit.xUnit;
using Xunit;

namespace Orleans.Transactions.Azure.Tests
{
    /// <summary>
    /// 测试使用 Azure Storage 时受控故障注入场景下的事务行为。
    /// </summary>
    [TestCategory("AzureStorage"), TestCategory("Transactions"), TestCategory("Functional")]
    [TestSuite("Functional")]
    [TestProvider("AzureStorage")]
    [TestArea("Transactions")]
    public class TransactionFaultInjectionTests : ControlledFaultInjectionTransactionTestRunnerxUnit, IClassFixture<ControlledFaultInjectionTestFixture>
    {
        public TransactionFaultInjectionTests(ControlledFaultInjectionTestFixture fixture, ITestOutputHelper output)
            : base(fixture.GrainFactory, output)
        {
            fixture.EnsurePreconditionsMet();
        }
    }
}
