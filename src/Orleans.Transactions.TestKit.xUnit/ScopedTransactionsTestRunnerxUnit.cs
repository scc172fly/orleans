using System.Threading.Tasks;

using Xunit;

namespace Orleans.Transactions.TestKit.xUnit
{
    /// <inheritdoc cref="ScopedTransactionsTestRunner"/>
    public abstract class ScopedTransactionsTestRunnerxUnit : ScopedTransactionsTestRunner
    {
        /// <summary>
        /// 初始化 <see cref="ScopedTransactionsTestRunnerxUnit"/> 类的新实例。
        /// </summary>
        /// <param name="grainFactory">下 grain 用于测试的工厂 grains.</param>
        /// <param name="transactionFrame">用于创建交易范围客户端。</param>
        /// <param name="output"> xUnit 测试输出助手。</param>
        protected ScopedTransactionsTestRunnerxUnit(IGrainFactory grainFactory, ITransactionClient transactionFrame, ITestOutputHelper output)
        : base(grainFactory, transactionFrame, TestOutputHelperExtensions.GetWriteLine(output, nameof(output))) { }

        /// <inheritdoc cref="ScopedTransactionsTestRunner.CreateTransactionScopeAndSetValue(string)"/>
        [Theory]
        [InlineData(TransactionTestConstants.SingleStateTransactionalGrain)]
        [InlineData(TransactionTestConstants.DoubleStateTransactionalGrain)]
        [InlineData(TransactionTestConstants.MaxStateTransactionalGrain)]
        public override Task CreateTransactionScopeAndSetValue(string grainStates)
        {
            return base.CreateTransactionScopeAndSetValue(grainStates);
        }

        /// <inheritdoc cref="ScopedTransactionsTestRunner.CreateTransactionScopeAndSetValueWithFailure(string)"/>
        [Theory]
        [InlineData(TransactionTestConstants.SingleStateTransactionalGrain)]
        [InlineData(TransactionTestConstants.DoubleStateTransactionalGrain)]
        [InlineData(TransactionTestConstants.MaxStateTransactionalGrain)]
        public override Task CreateTransactionScopeAndSetValueWithFailure(string grainStates)
        {
            return base.CreateTransactionScopeAndSetValueWithFailure(grainStates);
        }

        /// <inheritdoc cref="ScopedTransactionsTestRunner.CreateTransactionScopeAndSetValueAndAssert(string)"/>
        [Theory]
        [InlineData(TransactionTestConstants.SingleStateTransactionalGrain)]
        [InlineData(TransactionTestConstants.DoubleStateTransactionalGrain)]
        [InlineData(TransactionTestConstants.MaxStateTransactionalGrain)]
        public override Task CreateTransactionScopeAndSetValueAndAssert(string grainStates)
        {
            return base.CreateTransactionScopeAndSetValueAndAssert(grainStates);
        }

        /// <inheritdoc cref="ScopedTransactionsTestRunner.CreateNestedTransactionScopeAndSetValueAndInnerFailAndAssert(string)"/>
        [Theory]
        [InlineData(TransactionTestConstants.SingleStateTransactionalGrain)]
        [InlineData(TransactionTestConstants.DoubleStateTransactionalGrain)]
        [InlineData(TransactionTestConstants.MaxStateTransactionalGrain)]
        public override Task CreateNestedTransactionScopeAndSetValueAndInnerFailAndAssert(string grainStates)
        {
            return base.CreateNestedTransactionScopeAndSetValueAndInnerFailAndAssert(grainStates);
        }
    }
}
