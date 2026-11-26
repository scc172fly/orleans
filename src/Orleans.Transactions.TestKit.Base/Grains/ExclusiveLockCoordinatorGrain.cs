using Orleans.Concurrency;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 坐标 用于验证独家锁定行为 的读写时写交易模式
    /// </summary>
    [StatelessWorker]
    [Reentrant]
    public class ExclusiveLockCoordinatorGrain : Grain, IExclusiveLockCoordinatorGrain
    {
        /// <inheritdoc/>
        public async Task ReadThenWrite(ITransactionTestGrain grain, int value)
        {
            ArgumentNullException.ThrowIfNull(grain);
            await grain.Get();
            await Task.Delay(TimeSpan.FromMilliseconds(100)); // 添加一些延迟,使 tx 同时互插
            await grain.Add(value);
        }

        /// <inheritdoc/>
        public async Task ReadThenWriteWithExclusiveLock(IExclusiveLockTransactionTestGrain grain, int value)
        {
            ArgumentNullException.ThrowIfNull(grain);
            await grain.Get();
            await Task.Delay(TimeSpan.FromMilliseconds(100)); // 添加一些延迟,使 tx 同时互插
            await grain.Add(value);
        }
    }
}
