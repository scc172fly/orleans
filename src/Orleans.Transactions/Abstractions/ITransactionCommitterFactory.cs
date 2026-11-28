namespace Orleans.Transactions.Abstractions
{
    /// <summary>
    /// 为 Grain 激活实例创建事务提交器组件。
    /// </summary>
    public interface ITransactionCommitterFactory
    {
        /// <summary>
        /// 使用指定配置创建事务提交器。
        /// </summary>
        /// <typeparam name="TService">服务类型。</typeparam>
        /// <param name="config">事务提交器配置。</param>
        /// <returns>已配置的事务提交器。</returns>
        ITransactionCommitter<TService> Create<TService>(ITransactionCommitterConfiguration config) where TService : class;
    }
}
