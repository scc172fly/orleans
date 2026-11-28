namespace Orleans.Hosting
{
    /// <summary>
    /// 提供针对 <see cref="IClientBuilder"/> 的事务配置扩展方法。
    /// </summary>
    public static class ClientBuilderExtensions
    {
        /// <summary>
        /// 向客户端添加 Orleans 事务服务。
        /// </summary>
        /// <param name="builder">客户端构建器。</param>
        /// <returns>客户端构建器。</returns>
        public static IClientBuilder UseTransactions(this IClientBuilder builder)
            => builder.ConfigureServices(services => services.UseTransactionsWithClient());
    }
}
