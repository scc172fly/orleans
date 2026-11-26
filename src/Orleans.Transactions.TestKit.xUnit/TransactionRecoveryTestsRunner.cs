using System.Threading.Tasks;
using Orleans.TestingHost;
using Xunit;

namespace Orleans.Transactions.TestKit.xUnit
{
    /// <inheritdoc cref="TransactionRecoveryTestsRunner"/>
    public class TransactionRecoveryTestsRunnerxUnit : TransactionRecoveryTestsRunner
    {
        /// <summary>
        /// 初始化 <see cref="TransactionRecoveryTestsRunnerxUnit"/> 类的新实例。
        /// </summary>
        /// <param name="cluster">用于运行恢复设想的测试组群。</param>
        /// <param name="testOutput"> xUnit 测试输出助手。</param>
        public TransactionRecoveryTestsRunnerxUnit(TestCluster cluster, ITestOutputHelper testOutput)
            : base(cluster, TestOutputHelperExtensions.GetWriteLine(testOutput, nameof(testOutput)))
        {
        }

        /// <inheritdoc cref="TransactionRecoveryTestsRunner.TransactionWillRecoverAfterRandomSiloGracefulShutdown(string, int)"/>
        [Theory]
        [InlineData(TransactionTestConstants.SingleStateTransactionalGrain, 30)]
        [InlineData(TransactionTestConstants.DoubleStateTransactionalGrain, 20)]
        public override Task TransactionWillRecoverAfterRandomSiloGracefulShutdown(string transactionTestGrainClassName, int concurrent)
        {
            return base.TransactionWillRecoverAfterRandomSiloGracefulShutdown(transactionTestGrainClassName, concurrent);
        }

        /// <inheritdoc cref="TransactionRecoveryTestsRunner.TransactionWillRecoverAfterRandomSiloUnGracefulShutdown(string, int)"/>
        [Theory]
        [InlineData(TransactionTestConstants.SingleStateTransactionalGrain, 30)]
        [InlineData(TransactionTestConstants.DoubleStateTransactionalGrain, 20)]
        public override Task TransactionWillRecoverAfterRandomSiloUnGracefulShutdown(string transactionTestGrainClassName, int concurrent)
        {
            return base.TransactionWillRecoverAfterRandomSiloUnGracefulShutdown(transactionTestGrainClassName, concurrent);
        }

        /// <inheritdoc />
        protected override Task TransactionWillRecoverAfterRandomSiloFailure(
            string transactionTestGrainClassName,
            int concurrent,
            bool gracefulShutdown)
        {
            return base.TransactionWillRecoverAfterRandomSiloFailure(
                transactionTestGrainClassName,
                concurrent,
                gracefulShutdown,
                TestContext.Current.CancellationToken);
        }
    }
}
