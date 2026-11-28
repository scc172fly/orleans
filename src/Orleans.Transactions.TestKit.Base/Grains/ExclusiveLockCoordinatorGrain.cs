using Orleans.Concurrency;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 协调用于验证排他锁行为的先读后写事务模式。
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
            await Task.Delay(TimeSpan.FromMilliseconds(100)); // 增加一些延迟，使并发事务交错执行
            await grain.Add(value);
        }

        /// <inheritdoc/>
        public async Task ReadThenWriteWithExclusiveLock(IExclusiveLockTransactionTestGrain grain, int value)
        {
            ArgumentNullException.ThrowIfNull(grain);
            await grain.Get();
            await Task.Delay(TimeSpan.FromMilliseconds(100)); // 增加一些延迟，使并发事务交错执行
            await grain.Add(value);
        }
    }
}
