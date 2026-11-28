using System.Threading.Tasks;
using Xunit;

namespace Orleans.Transactions.TestKit.xUnit
{
    /// <inheritdoc cref="GrainFaultTransactionTestRunner"/>
    public class GrainFaultTransactionTestRunnerxUnit : GrainFaultTransactionTestRunner
    {
        /// <summary>
        /// 初始化 <see cref="GrainFaultTransactionTestRunnerxUnit"/> 类的新实例。
        /// </summary>
        /// <param name="grainFactory">用于访问测试 Grain 的 Grain 工厂。</param>
        /// <param name="output">xUnit 测试输出助手。</param>
        public GrainFaultTransactionTestRunnerxUnit(IGrainFactory grainFactory, ITestOutputHelper output)
        : base(grainFactory, TestOutputHelperExtensions.GetWriteLine(output, nameof(output)))
        { }

        /// <inheritdoc cref="GrainFaultTransactionTestRunner.AbortTransactionOnExceptions(string)"/>
        [Theory]
        [InlineData(TransactionTestConstants.SingleStateTransactionalGrain)]
        [InlineData(TransactionTestConstants.DoubleStateTransactionalGrain)]
        [InlineData(TransactionTestConstants.MaxStateTransactionalGrain)]
        public override Task AbortTransactionOnExceptions(string grainStates)
        {
            return base.AbortTransactionOnExceptions(grainStates);
        }

        /// <inheritdoc cref="GrainFaultTransactionTestRunner.AbortTransactionOnReadOnlyViolatedException(string)"/>
        [Theory]
        [InlineData(TransactionTestConstants.SingleStateTransactionalGrain)]
        [InlineData(TransactionTestConstants.DoubleStateTransactionalGrain)]
        [InlineData(TransactionTestConstants.MaxStateTransactionalGrain)]
        public override Task AbortTransactionOnReadOnlyViolatedException(string grainStates)
        {
            return base.AbortTransactionOnReadOnlyViolatedException(grainStates);
        }

        /// <inheritdoc cref="GrainFaultTransactionTestRunner.MultiGrainAbortTransactionOnExceptions(string)"/>
        [Theory]
        [InlineData(TransactionTestConstants.SingleStateTransactionalGrain)]
        [InlineData(TransactionTestConstants.DoubleStateTransactionalGrain)]
        [InlineData(TransactionTestConstants.MaxStateTransactionalGrain)]
        public override Task MultiGrainAbortTransactionOnExceptions(string grainStates)
        {
            return base.MultiGrainAbortTransactionOnExceptions(grainStates);
        }

        /// <inheritdoc cref="GrainFaultTransactionTestRunner.AbortTransactionExceptionInnerExceptionOnlyContainsOneRootCauseException(string)"/>
        [Theory]
        [InlineData(TransactionTestConstants.SingleStateTransactionalGrain)]
        [InlineData(TransactionTestConstants.DoubleStateTransactionalGrain)]
        [InlineData(TransactionTestConstants.MaxStateTransactionalGrain)]
        public override Task AbortTransactionExceptionInnerExceptionOnlyContainsOneRootCauseException(string grainStates)
        {
            return base.AbortTransactionExceptionInnerExceptionOnlyContainsOneRootCauseException(grainStates);
        }

        /// <inheritdoc cref="GrainFaultTransactionTestRunner.AbortTransactionOnOrphanCalls(string)"/>
        [Theory()]
        [InlineData(TransactionTestConstants.SingleStateTransactionalGrain)]
        [InlineData(TransactionTestConstants.DoubleStateTransactionalGrain)]
        [InlineData(TransactionTestConstants.MaxStateTransactionalGrain)]
        public override Task AbortTransactionOnOrphanCalls(string grainStates)
        {
            return base.AbortTransactionOnOrphanCalls(grainStates);
        }

    }
}
