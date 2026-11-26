using Orleans.EventSourcing;
using Orleans.Providers;
using TestGrainInterfaces;

namespace TestGrains
{
    /// <summary>
    /// 例如,一个以银行账户为模型的日记 grain 。
    /// 
    /// 配置后使用默认的存储提供者。
    /// 配置后使用 LogStorage 一致性提供商。
    /// 
    /// 提供商坚持所有事件,使我们能够收回所有事件。
    /// </summary>

    [StorageProvider(ProviderName = "Default")]
    [LogConsistencyProvider(ProviderName = "LogStorage")]

    public class AccountGrain : JournaledGrain<AccountGrain.GrainState, Transaction>, IAccountGrain
    {
        /// <summary>
        /// 现状 grain 仅仅是当前平衡。
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

            // 我们等待储存的当
            return ConfirmEvents();
        }

        public Task<bool> Withdraw(uint amount, Guid guid, string description)
        {
            // 如果余额太低,不能退
            // 立即拒绝
            if (State.Balance < amount)
                return Task.FromResult(false);

            // 使用有条件退出事件
            // (有条件的事件只有在版本尚未改变的情况下才会发生)
            // 这一点很重要,所以我们才能保证 我们不会过度借债
            // 即使与其他组群赛跑,但属于短暂重复 grain 情况
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


    /// 同一 grain 的变异,该变异不持续日志,而仅维持最新的 grain 状态
    /// (因此它并不真正产生事件来源) 
    [LogConsistencyProvider(ProviderName = "StateStorage")]
    public class AccountGrain_PersistStateOnly : AccountGrain
    {
    }
}
