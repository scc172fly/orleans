using AwesomeAssertions;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 验证事务使用共享锁和显式请求的排他锁时的行为。
    /// </summary>
    public abstract class ExclusiveLockTransactionTestRunner : TransactionTestRunnerBase
    {
        /// <summary>
        /// 初始化 <see cref="ExclusiveLockTransactionTestRunner"/> 类的新实例。
        /// </summary>
        /// <param name="grainFactory">用于访问测试 Grain 的 Grain 工厂。</param>
        /// <param name="output">用于写入测试输出的回调。</param>
        protected ExclusiveLockTransactionTestRunner(IGrainFactory grainFactory, Action<string> output)
            : base(grainFactory, output)
        {
        }

        /// <summary>
        /// 验证未使用排他锁时，在同一个 Grain 上并发执行先读后写的事务
        /// 会引发 OrleansTransactionLockUpgradeException 或 OrleansBrokenTransactionLockException
        /// 异常。
        ///
        /// 场景（LockUpgradeException）：
        /// TX1：PerformRead → 获取共享锁
        /// TX2：PerformRead → 获取共享锁（同一锁组）
        /// TX1：PerformUpdate → 锁升级失败
        ///
        /// 场景（BrokenTransactionLockException）：
        /// TX1：PerformRead → 获取共享锁
        /// TX2：PerformRead → 获取共享锁（同一锁组）
        /// TX2：PerformUpdate → 锁升级成功，TX1 回滚
        /// TX1：PerformUpdate → 检测到已失效的锁
        /// </summary>
        /// <param name="grainStates">用于选择测试 Grain 的事务状态配置。</param>
        /// <returns>表示并发事务测试的任务。</returns>
        public virtual async Task ConcurrentReadThenWriteWithoutExclusiveLock_ThrowsLockException(string grainStates)
        {
            const int concurrentTransactions = 10;
            const int addValue = 5;

            ITransactionTestGrain grain = RandomTestGrain(grainStates);

            var tasks = new List<Task>(concurrentTransactions);
            for (int i = 0; i < concurrentTransactions; i++)
            {
                var coordinator = this.grainFactory.GetGrain<IExclusiveLockCoordinatorGrain>(Guid.NewGuid());
                tasks.Add(coordinator.ReadThenWrite(grain, addValue));
            }

            var exceptions = new List<Exception>();
            foreach (var task in tasks)
            {
                try
                {
                    await task;
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }

            this.testOutput($"Total exceptions: {exceptions.Count} out of {concurrentTransactions} transactions");
            foreach (var ex in exceptions)
            {
                this.testOutput($"  Exception type: {ex.GetType().Name}");
            }

            exceptions.Should().Contain(ex =>
                ex is OrleansTransactionLockUpgradeException
                || ex is OrleansBrokenTransactionLockException);

            var values = await grain.Get();
            values[0].Should().Be((concurrentTransactions - exceptions.Count) * addValue);
        }

        /// <summary>
        /// 验证在读取方法上使用 [UseExclusiveLock] 时，
        /// 能够避免并发事务在同一个 Grain 上执行先读后写操作时发生锁升级异常。
        /// </summary>
        /// <param name="grainStates">测试使用的事务状态配置。</param>
        /// <returns>表示并发事务测试的任务。</returns>
        public virtual async Task ConcurrentReadThenWriteWithExclusiveLock_NoLockException(string grainStates)
        {
            const int concurrentTransactions = 10;
            const int addValue = 5;

            var grain = RandomTestGrain<IExclusiveLockTransactionTestGrain>(
                TransactionTestConstants.ExclusiveLockTransactionTestGrain);

            var tasks = new List<Task>(concurrentTransactions);
            for (int i = 0; i < concurrentTransactions; i++)
            {
                var coordinator = this.grainFactory.GetGrain<IExclusiveLockCoordinatorGrain>(Guid.NewGuid());
                tasks.Add(coordinator.ReadThenWriteWithExclusiveLock(grain, addValue));
            }

            var lockExceptions = new List<Exception>();
            foreach (var task in tasks)
            {
                try
                {
                    await task;
                }
                catch (OrleansTransactionLockUpgradeException ex)
                {
                    lockExceptions.Add(ex);
                }
                catch (OrleansBrokenTransactionLockException ex)
                {
                    lockExceptions.Add(ex);
                }
            }

            this.testOutput($"Lock exceptions: {lockExceptions.Count} out of {concurrentTransactions} transactions");

            // 不应发生锁升级异常或锁失效异常
            lockExceptions.Should().BeEmpty("UseExclusiveLock should prevent lock upgrade conflicts by acquiring exclusive locks from the start");

            // 验证 Grain 状态的一致性：所有成功事务都应已增加各自的数值
            int[] values = await grain.Get();
            this.testOutput($"Final grain value: {values[0]}");
            (values[0]).Should().Be(concurrentTransactions * addValue);
        }
    }
}
