using System.Threading;

namespace Orleans.Transactions
{
    /// <summary>
    /// 提供与当前非同步执行环境有关的交易的准入。
    /// </summary>
    public static class TransactionContext
    {
        private static readonly AsyncLocal<TransactionInfo?> CurrentContext = new();

        /// <summary>
        /// 获取当前交易信息 。
        /// </summary>
        /// <returns>当前交易信息,或无交易活动时的 <see langword="null"/> 。</returns>
        public static TransactionInfo? GetTransactionInfo() => CurrentContext.Value;

        /// <summary>
        /// 获取当前交易的识别符号。
        /// </summary>
        /// <value>当前的交易识别符 。</value>
        /// <exception cref="OrleansTransactionException">当前上下文中没有交易。</exception>
        public static string CurrentTransactionId => GetRequiredTransactionInfo().Id;

        /// <summary>
        /// 获取当前交易信息 。
        /// </summary>
        /// <returns>当前的交易信息。</returns>
        /// <exception cref="OrleansTransactionException">当前上下文中没有交易。</exception>
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
