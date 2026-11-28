using Orleans.EventSourcing;
using Orleans.Providers;
using TestGrainInterfaces;

namespace TestGrains
{
    /// <summary>
    /// 对银行账户进行建模的日志化 Grain 示例。
    /// 
    /// 配置为使用默认存储提供程序。
    /// 配置为使用 LogStorage 一致性提供程序。
    /// 
    /// 此提供程序持久化所有事件，并允许我们检索全部事件。
    /// </summary>

    [StorageProvider(ProviderName = "Default")]
    [LogConsistencyProvider(ProviderName = "LogStorage")]

    public class AccountGrain : JournaledGrain<AccountGrain.GrainState, Transaction>, IAccountGrain
    {
        /// <summary>
        /// 此 Grain 的状态仅为当前余额。
        /// </summary>
        [Serializable]
        [Orleans.GenerateSerializer]
        public class GrainState
        {
            [Orleans.Id(0)]
            public uint Balance { get; set; }

            public void Apply(DepositTransaction d)
            {
                Balance = Balance + d.DepositAmount;
            }

            public void Apply(WithdrawalTransaction d)
            {
                if (d.WithdrawalAmount > Balance)
                    throw new InvalidOperationException("we make sure this never happens");

                Balance = Balance - d.WithdrawalAmount;
            }
        }

        public Task<uint> Balance()
        {
            return Task.FromResult(State.Balance);
        }

        public Task Deposit(uint amount, Guid guid, string description)
        {
            RaiseEvent(new DepositTransaction()
            {
                Guid = guid,
                IssueTime = DateTime.UtcNow,
                DepositAmount = amount,
                Description = description
            });

            // 等待存储确认
            return ConfirmEvents();
        }

        public Task<bool> Withdraw(uint amount, Guid guid, string description)
        {
            // 如果余额不足，则不能提款
            // 立即拒绝该请求
            if (State.Balance < amount)
                return Task.FromResult(false);

            // 使用条件事件进行提款
            // （只有版本在此期间尚未改变时，条件事件才会提交）
            // 这一点很重要，它可以保证我们绝不会透支，
            // 即使与其他集群发生竞争或临时出现重复 Grain 激活实例，也能如此
            return RaiseConditionalEvent(new WithdrawalTransaction()
            {
                Guid = guid,
                IssueTime = DateTime.UtcNow,
                WithdrawalAmount = amount,
                Description = description
            });
        }

        public Task<IReadOnlyList<Transaction>> GetTransactionLog()
        {
            return RetrieveConfirmedEvents(0, Version);
        }
    }


    /// 同一 Grain 的一种变体，不持久化日志，仅持久化最新 Grain 状态
    /// （因此不是真正的事件溯源）。
    [LogConsistencyProvider(ProviderName = "StateStorage")]
    public class AccountGrain_PersistStateOnly : AccountGrain
    {
    }
}
