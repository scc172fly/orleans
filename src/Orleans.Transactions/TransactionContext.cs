using System.Threading;

namespace Orleans.Transactions
{
    /// <summary>
    /// 提供访问与当前异步执行上下文关联的事务的功能。
    /// </summary>
    public static class TransactionContext
    {
        private static readonly AsyncLocal<TransactionInfo?> CurrentContext = new();

        /// <summary>
        /// 获取当前事务信息。
        /// </summary>
        /// <returns>当前事务信息；如果没有活跃事务，则为 <see langword="null"/>。</returns>
        public static TransactionInfo? GetTransactionInfo() => CurrentContext.Value;

        /// <summary>
        /// 获取当前事务的标识符。
        /// </summary>
        /// <value>当前事务标识符。</value>
        /// <exception cref="OrleansTransactionException">当前上下文中不存在事务。</exception>
        public static string CurrentTransactionId => GetRequiredTransactionInfo().Id;

        /// <summary>
        /// 获取当前事务信息。
        /// </summary>
        /// <returns>当前事务信息。</returns>
        /// <exception cref="OrleansTransactionException">当前上下文中不存在事务。</exception>
        public static TransactionInfo GetRequiredTransactionInfo() => GetTransactionInfo() ?? throw new OrleansTransactionException($"A transaction context is required for access. Did you forget a [Transaction] attribute?");

        internal static void SetTransactionInfo(TransactionInfo? info)
        {
            if (!ReferenceEquals(CurrentContext.Value, info))
            {
                CurrentContext.Value = info;
            }
        }

        internal static void Clear() => CurrentContext.Value = null;
    }
}
