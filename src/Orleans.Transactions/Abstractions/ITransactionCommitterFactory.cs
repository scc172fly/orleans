namespace Orleans.Transactions.Abstractions
{
    /// <summary>
    /// 为 grain 激活创建交易承诺方方面面。
    /// </summary>
    public interface ITransactionCommitterFactory
    {
        /// <summary>
        /// 使用指定配置创建交易承诺方。
        /// </summary>
        /// <typeparam name="TService">服务类型。</typeparam>
        /// <param name="config">交易承诺方配置。</param>
        /// <returns>配置的交易承诺方。</returns>
        ITransactionCommitter<TService> Create<TService>(ITransactionCommitterConfiguration config) where TService : class;
    }
}
