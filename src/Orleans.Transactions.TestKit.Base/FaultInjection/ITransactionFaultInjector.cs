namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 在存储事务状态之前或之后注入故障。
    /// </summary>
    public interface ITransactionFaultInjector
    {
        /// <summary>
        /// 在存储事务状态之前注入故障。
        /// </summary>
        void BeforeStore();

        /// <summary>
        /// 在存储事务状态之后注入故障。
        /// </summary>
        void AfterStore();
    }
}
