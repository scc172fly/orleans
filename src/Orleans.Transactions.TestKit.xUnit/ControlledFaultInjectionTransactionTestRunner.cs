using System.Threading.Tasks;
using Xunit;

namespace Orleans.Transactions.TestKit.xUnit
{
    /// <inheritdoc cref="ControlledFaultInjectionTransactionTestRunner"/>
    public class ControlledFaultInjectionTransactionTestRunnerxUnit : ControlledFaultInjectionTransactionTestRunner
    {
        /// <summary>
        /// 初始化 <see cref="ControlledFaultInjectionTransactionTestRunnerxUnit"/> 类的新实例。
        /// </summary>
        /// <param name="grainFactory">用于访问测试 Grain 的 Grain 工厂。</param>
        /// <param name="output">xUnit 测试输出助手。</param>
        public ControlledFaultInjectionTransactionTestRunnerxUnit(IGrainFactory grainFactory, ITestOutputHelper output)
         : base(grainFactory, TestOutputHelperExtensions.GetWriteLine(output, nameof(output)))
        { }

        /// <inheritdoc cref="ControlledFaultInjectionTransactionTestRunner.SingleGrainReadTransaction"/>
        [Fact]
        public override Task SingleGrainReadTransaction()
        {
            return base.SingleGrainReadTransaction();
        }

        /// <inheritdoc cref="ControlledFaultInjectionTransactionTestRunner.SingleGrainWriteTransaction"/>
        [Fact]
        public override Task SingleGrainWriteTransaction()
        {
            return base.SingleGrainWriteTransaction();
        }

        /// <inheritdoc cref="ControlledFaultInjectionTransactionTestRunner.MultiGrainWriteTransaction_FaultInjection(TransactionFaultInjectPhase, FaultInjectionType)"/>
        [Theory]
        [InlineData(TransactionFaultInjectPhase.AfterPrepare, FaultInjectionType.Deactivation)]
        [InlineData(TransactionFaultInjectPhase.AfterConfirm, FaultInjectionType.Deactivation)]
        [InlineData(TransactionFaultInjectPhase.AfterPrepared, FaultInjectionType.Deactivation)]
        [InlineData(TransactionFaultInjectPhase.AfterPrepareAndCommit, FaultInjectionType.Deactivation)]
        [InlineData(TransactionFaultInjectPhase.BeforePrepare, FaultInjectionType.ExceptionAfterStore)]
        [InlineData(TransactionFaultInjectPhase.BeforePrepare, FaultInjectionType.ExceptionBeforeStore)]
        [InlineData(TransactionFaultInjectPhase.BeforeConfirm, FaultInjectionType.ExceptionAfterStore)]
        [InlineData(TransactionFaultInjectPhase.BeforeConfirm, FaultInjectionType.ExceptionBeforeStore)]
        [InlineData(TransactionFaultInjectPhase.BeforePrepareAndCommit, FaultInjectionType.ExceptionAfterStore)]
        [InlineData(TransactionFaultInjectPhase.BeforePrepareAndCommit, FaultInjectionType.ExceptionBeforeStore)]
        public override Task MultiGrainWriteTransaction_FaultInjection(TransactionFaultInjectPhase injectionPhase, FaultInjectionType injectionType)
        {
            return base.MultiGrainWriteTransaction_FaultInjection(injectionPhase, injectionType);
        }
    }
}
