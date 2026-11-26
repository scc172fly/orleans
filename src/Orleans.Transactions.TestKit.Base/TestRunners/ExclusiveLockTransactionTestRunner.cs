using AwesomeAssertions;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 使用共享锁和明确要求的独家锁定验证交易行为。
    /// </summary>
    public abstract class ExclusiveLockTransactionTestRunner : TransactionTestRunnerBase
    {
        /// <summary>
        /// 初始化 <see cref="ExclusiveLockTransactionTestRunner"/> 类的新实例。
        /// </summary>
        /// <param name="grainFactory">下 grain 用于测试的工厂 grains.</param>
        /// <param name="output">用于写入测试输出的回调。</param>
        protected ExclusiveLockTransactionTestRunner(IGrainFactory grainFactory, Action<string> output)
            : base(grainFactory, output)
        {
        }

        /// <summary>
        /// 证明在同一页上同时进行读出时写交易 grain
        /// 直径 OrleansTransactionLockUpgradeException 或 OrleansBrokenTransactionLockException
        /// 当不使用独家锁时。
        ///
        /// 假设情景(LockUpgradeException):
        ///   TX1: PerformRead 获得共享锁
        ///   TX2: PerformRead 获得共享锁( 相同组)
        ///   TX1: PerformUpdate 锁定升级失败
        ///
        /// 假设情景(BrokenTransactionLockException):
        ///   TX1: PerformRead 获得共享锁
        ///   TX2: PerformRead 获得共享锁( 相同组)
        ///   TX2: PerformUpdate +______________________升级, TX1 倒滚
        ///   TX1: PerformUpdate 检测到 破锁
        /// </summary>
        /// <param name="grainStates">用于选择测试 grain的交易状态配置。</param>
        /// <returns>一项任务,代表同时进行的交易测试。</returns>
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
        /// 证明在读作方法上使用[UseExclusiveLock防止
        /// 当同时进行的交易在同一 grain上执行“读作”时,有锁定升级例外。
        /// </summary>
        /// <param name="grainStates">测试所使用的交易状态配置。</param>
        /// <returns>一项任务,代表同时进行的交易测试。</returns>
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

            // 不应出现任何锁升级或破锁例外
            lockExceptions.Should().BeEmpty("UseExclusiveLock should prevent lock upgrade conflicts by acquiring exclusive locks from the start");

            // 验证 grain 状态的一致性 - 所有成功交易都应该增加其价值
            int[] values = await grain.Get();
            this.testOutput($"Final grain value: {values[0]}");
            (values[0]).Should().Be(concurrentTransactions * addValue);
        }
    }
}
