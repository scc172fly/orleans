namespace TestGrainInterfaces
{
    /// <summary>
    /// 对银行账户进行建模的 Grain
    /// </summary>
    public interface IAccountGrain : IGrainWithStringKey
    {
        Task<uint> Balance();

        Task Deposit(uint amount, Guid guid, string desc);

        Task<bool> Withdraw(uint amount, Guid guid, string desc);

        Task<IReadOnlyList<Transaction>> GetTransactionLog();
    }

    // 以下类表示账户上的事件/交易
    // 所有字段均由用户定义（没有任何字段具有特殊含义），
    // 因此可以采用任意所需的对象类型，只要能够序列化即可
    // （这样就可以通过网络传输并持久化到日志中）。

    [Serializable]
    [GenerateSerializer]
    public abstract class Transaction
    {
        /// <summary> 此交易的唯一标识符 </summary>
        [Id(0)]
        public Guid Guid { get; set; }

        /// <summary> 此交易的说明 </summary>
        [Id(1)]
        public string? Description { get; set; }

        /// <summary> 请求进入系统的时间 </summary>
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
