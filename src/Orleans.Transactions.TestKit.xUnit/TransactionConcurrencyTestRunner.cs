using System.Threading.Tasks;
using Xunit;

namespace Orleans.Transactions.TestKit.xUnit
{
    /// <inheritdoc cref="TransactionConcurrencyTestRunner"/>
    public abstract class TransactionConcurrencyTestRunnerxUnit : TransactionConcurrencyTestRunner
    {
        /// <summary>
        /// 初始化 <see cref="TransactionConcurrencyTestRunnerxUnit"/> 类的新实例。
        /// </summary>
        /// <param name="grainFactory">下 grain 用于测试的工厂 grains.</param>
        /// <param name="output"> xUnit 测试输出助手。</param>
        protected TransactionConcurrencyTestRunnerxUnit(IGrainFactory grainFactory, ITestOutputHelper output)
        : base(grainFactory, TestOutputHelperExtensions.GetWriteLine(output, nameof(output))) { }

        /// <inheritdoc cref="TransactionConcurrencyTestRunner.SingleSharedGrainTest(string)"/>
        [Theory]
        [InlineData(TransactionTestConstants.SingleStateTransactionalGrain)]
        [InlineData(TransactionTestConstants.DoubleStateTransactionalGrain)]
        [InlineData(TransactionTestConstants.MaxStateTransactionalGrain)]
        public override Task SingleSharedGrainTest(string grainStates)
        {
            return base.SingleSharedGrainTest(grainStates);
        }

        /// <inheritdoc cref="TransactionConcurrencyTestRunner.TransactionChainTest(string)"/>
        [Theory]
        [InlineData(TransactionTestConstants.SingleStateTransactionalGrain)]
        [InlineData(TransactionTestConstants.DoubleStateTransactionalGrain)]
        [InlineData(TransactionTestConstants.MaxStateTransactionalGrain)]
        public override Task TransactionChainTest(string grainStates)
        {
            return base.TransactionChainTest(grainStates);
        }

        /// <inheritdoc cref="TransactionConcurrencyTestRunner.TransactionTreeTest(string)"/>
        [Theory]
        [InlineData(TransactionTestConstants.SingleStateTransactionalGrain)]
        [InlineData(TransactionTestConstants.DoubleStateTransactionalGrain)]
        [InlineData(TransactionTestConstants.MaxStateTransactionalGrain)]
        public override Task TransactionTreeTest(string grainStates)
        {
            return base.TransactionTreeTest(grainStates);
        }
    }
}
