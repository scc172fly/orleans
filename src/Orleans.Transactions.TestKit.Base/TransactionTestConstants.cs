
namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 提供事务测试工具包使用的提供程序和 Grain 实现名称。
    /// </summary>
    public static class TransactionTestConstants
    {
        /// <summary>
        /// 协同事务测试场景所包含的 Grain 最大数量。
        /// </summary>
        public const int MaxCoordinatedTransactions = 8;

        // 存储提供程序
        /// <summary>
        /// 测试 Grain 使用的事务状态存储提供程序的名称。
        /// </summary>
        public const string TransactionStore = "TransactionStore";

        // 提交器服务
        /// <summary>
        /// 事务提交器测试使用的远程提交服务的名称。
        /// </summary>
        public const string RemoteCommitService = "RemoteCommitService";

        // Grain 实现
        /// <summary>
        /// 不含事务状态的事务测试 Grain 类名。
        /// </summary>
        public const string NoStateTransactionalGrain = "NoStateTransactionalGrain";

        /// <summary>
        /// 具有一个事务状态的事务测试 Grain 类名。
        /// </summary>
        public const string SingleStateTransactionalGrain = "SingleStateTransactionalGrain";

        /// <summary>
        /// 具有两个事务状态的事务测试 Grain 类名。
        /// </summary>
        public const string DoubleStateTransactionalGrain = "DoubleStateTransactionalGrain";

        /// <summary>
        /// 具有测试工具包所支持最大状态数量的事务测试 Grain 类名。
        /// </summary>
        public const string MaxStateTransactionalGrain = "MaxStateTransactionalGrain";

        /// <summary>
        /// 用于验证排他锁行为的事务测试 Grain 的类名。
        /// </summary>
        public const string ExclusiveLockTransactionTestGrain = "ExclusiveLockTransactionTestGrain";
    }
}
