namespace TestGrainInterfaces
{
    /// <summary>
    /// 模拟银行账户的 grain
    /// </summary>
    public interface IAccountGrain : IGrainWithStringKey
    {
        Task<uint> Balance();

        Task Deposit(uint amount, Guid guid, string desc);

        Task<bool> Withdraw(uint amount, Guid guid, string desc);

        Task<IReadOnlyList<Transaction>> GetTransactionLog();
    }

    // 下面的分类代表账户上的事件/交易
    // 全部字段都是自定义的(无特殊含义),
    // 这些都是任何类型的物体 只要它们可以连号
    // 以便他们被送到铁丝上,而永居其中。

    [Serializable]
    [GenerateSerializer]
    public abstract class Transaction
    {
        /// <summary> 此交易的独有标识符 </summary>
        [Id(0)]
        public Guid Guid { get; set; }

        /// <summary> 交易说明 </summary>
        [Id(1)]
        public string? Description { get; set; }

        /// <summary> 请求进入系统的时间  </summary>
        [Id(2)]
        public DateTime IssueTime { get; set; }
    }

    [Serializable]
    [GenerateSerializer]
    public class DepositTransaction : Transaction
    {
        [Id(0)]
        public uint DepositAmount { get; set; }
    }

    [Serializable]
    [GenerateSerializer]
    public class WithdrawalTransaction : Transaction
    {
        [Id(0)]
        public uint WithdrawalAmount { get; set; }
    }

}
