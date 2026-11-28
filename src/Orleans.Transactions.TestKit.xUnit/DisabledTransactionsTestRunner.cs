using Xunit;

namespace Orleans.Transactions.TestKit.xUnit
{
    /// <inheritdoc cref="DisabledTransactionsTestRunner"/>
    public class DisabledTransactionsTestRunnerxUnit : DisabledTransactionsTestRunner
    {
        /// <summary>
        /// 初始化 <see cref="DisabledTransactionsTestRunnerxUnit"/> 类的新实例。
        /// </summary>
        /// <param name="grainFactory">用于访问测试 Grain 的 Grain 工厂。</param>
        /// <param name="output">xUnit 测试输出助手。</param>
        protected DisabledTransactionsTestRunnerxUnit(IGrainFactory grainFactory, ITestOutputHelper output)
            : base(grainFactory, TestOutputHelperExtensions.GetWriteLine(output, nameof(output))) { }

        /// <inheritdoc cref="DisabledTransactionsTestRunner.TransactionGrainsThrowWhenTransactions(string)"/>
        [Theory]
        [InlineData(TransactionTestConstants.NoStateTransactionalGrain)]
        public override void TransactionGrainsThrowWhenTransactions(string transactionTestGrainClassName)
        {
            base.TransactionGrainsThrowWhenTransactions(transactionTestGrainClassName);
        }

        /// <inheritdoc cref="DisabledTransactionsTestRunner.MultiTransactionGrainsThrowWhenTransactions(string)"/>
        [Theory]
        [InlineData(TransactionTestConstants.NoStateTransactionalGrain)]
        public override void MultiTransactionGrainsThrowWhenTransactions(string transactionTestGrainClassName)
        {
            base.MultiTransactionGrainsThrowWhenTransactions(transactionTestGrainClassName);
        }
    }
}
