namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 在交易状态储存之前或之后,注入错误。
    /// </summary>
    public interface ITransactionFaultInjector
    {
        /// <summary>
        /// 在交易状态被储存之前就注入故障。
        /// </summary>
        void BeforeStore();

        /// <summary>
        /// 在交易状态被储存后,会引发过失。
        /// </summary>
        void AfterStore();
    }
}
