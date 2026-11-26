
namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 提供交易测试工具包所使用的提供者和 grain 执行名称。
    /// </summary>
    public static class TransactionTestConstants
    {
        /// <summary>
        /// 协调交易测试设想方案所包含的 grains 最大数量。
        /// </summary>
        public const int MaxCoordinatedTransactions = 8;

        // 储存存储供应商
        /// <summary>
        /// 试验 grains所用交易性国家储存提供者的名称。
        /// </summary>
        public const string TransactionStore = "TransactionStore";

        // 委托人服务
        /// <summary>
        /// 交易承诺测试所使用的远程连接服务的名称 。
        /// </summary>
        public const string RemoteCommitService = "RemoteCommitService";

        // grain 执行
        /// <summary>
        /// 交易测试的类别名称 grain 没有交易状态。
        /// </summary>
        public const string NoStateTransactionalGrain = "NoStateTransactionalGrain";

        /// <summary>
        /// 交易测试 grain 的类名称,只有一个交易状态。
        /// </summary>
        public const string SingleStateTransactionalGrain = "SingleStateTransactionalGrain";

        /// <summary>
        /// 交易测试 grain 类别名称,有两个交易状态。
        /// </summary>
        public const string DoubleStateTransactionalGrain = "DoubleStateTransactionalGrain";

        /// <summary>
        /// 交易测试 grain 的类名称,加上最大测试- Kit 状态计数。
        /// </summary>
        public const string MaxStateTransactionalGrain = "MaxStateTransactionalGrain";

        /// <summary>
        /// 用于核查独家锁定的交易测试 grain 类名 。
        /// </summary>
        public const string ExclusiveLockTransactionTestGrain = "ExclusiveLockTransactionTestGrain";
    }
}
