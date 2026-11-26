using System.Threading.Tasks;
using Xunit;

namespace Orleans.Transactions.TestKit.xUnit
{
    /// <inheritdoc cref="TocFaultTransactionTestRunner"/>
    public abstract class TocFaultTransactionTestRunnerxUnit : TocFaultTransactionTestRunner
    {
        /// <summary>
        /// 初始化 <see cref="TocFaultTransactionTestRunnerxUnit"/> 类的新实例。
        /// </summary>
        /// <param name="grainFactory">下 grain 用于测试的工厂 grains.</param>
        /// <param name="output"> xUnit 测试输出助手。</param>
        protected TocFaultTransactionTestRunnerxUnit(IGrainFactory grainFactory, ITestOutputHelper output)
        : base(grainFactory, TestOutputHelperExtensions.GetWriteLine(output, nameof(output))) { }

        /// <inheritdoc cref="TocFaultTransactionTestRunner.MultiGrainWriteTransactionWithCommitFailure(string, int)"/>
        [Theory]
        [InlineData(TransactionTestConstants.SingleStateTransactionalGrain, TransactionTestConstants.MaxCoordinatedTransactions)]
        [InlineData(TransactionTestConstants.DoubleStateTransactionalGrain, TransactionTestConstants.MaxCoordinatedTransactions / 2)]
        [InlineData(TransactionTestConstants.MaxStateTransactionalGrain, 1)]
        public override Task MultiGrainWriteTransactionWithCommitFailure(string grainStates, int grainCount)
        {
            return base.MultiGrainWriteTransactionWithCommitFailure(grainStates, grainCount);
        }

        /// <inheritdoc cref="TocFaultTransactionTestRunner.MultiGrainWriteTransactionWithCommitException(string, int)"/>
        [Theory]
        [InlineData(TransactionTestConstants.SingleStateTransactionalGrain, TransactionTestConstants.MaxCoordinatedTransactions)]
        [InlineData(TransactionTestConstants.DoubleStateTransactionalGrain, TransactionTestConstants.MaxCoordinatedTransactions / 2)]
        [InlineData(TransactionTestConstants.MaxStateTransactionalGrain, 1)]
        public override Task MultiGrainWriteTransactionWithCommitException(string grainStates, int grainCount)
        {
            return base.MultiGrainWriteTransactionWithCommitException(grainStates, grainCount);
        }
    }
}
