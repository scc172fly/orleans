using Xunit;

namespace Orleans.Transactions.TestKit.xUnit
{
    /// <inheritdoc cref="ExclusiveLockTransactionTestRunner"/>
    public abstract class ExclusiveLockTransactionTestRunnerxUnit : ExclusiveLockTransactionTestRunner
    {
        /// <summary>
        /// 初始化 <see cref="ExclusiveLockTransactionTestRunnerxUnit"/> 类的新实例。
        /// </summary>
        /// <param name="grainFactory">下 grain 用于测试的工厂 grains.</param>
        /// <param name="output"> xUnit 测试输出助手。</param>
        protected ExclusiveLockTransactionTestRunnerxUnit(IGrainFactory grainFactory, ITestOutputHelper output)
            : base(grainFactory, TestOutputHelperExtensions.GetWriteLine(output, nameof(output)))
        {
        }

        /// <inheritdoc cref="ExclusiveLockTransactionTestRunner.ConcurrentReadThenWriteWithoutExclusiveLock_ThrowsLockException(string)"/>
        [Theory]
        [InlineData(TransactionTestConstants.SingleStateTransactionalGrain)]
        public override Task ConcurrentReadThenWriteWithoutExclusiveLock_ThrowsLockException(string grainStates)
        {
            return base.ConcurrentReadThenWriteWithoutExclusiveLock_ThrowsLockException(grainStates);
        }

        /// <inheritdoc cref="ExclusiveLockTransactionTestRunner.ConcurrentReadThenWriteWithExclusiveLock_NoLockException(string)"/>
        [Theory]
        [InlineData(TransactionTestConstants.SingleStateTransactionalGrain)]
        public override Task ConcurrentReadThenWriteWithExclusiveLock_NoLockException(string grainStates)
        {
            return base.ConcurrentReadThenWriteWithExclusiveLock_NoLockException(grainStates);
        }
    }
}
