namespace Orleans.Hosting
{
    /// <summary>
    /// 提供 <see cref="IClientBuilder"/>的交易配置扩展。
    /// </summary>
    public static class ClientBuilderExtensions
    {
        /// <summary>
        /// 向客户端添加 Orleans 交易服务。
        /// </summary>
        /// <param name="builder">客户端构建器。</param>
        /// <returns>客户端构建器。</returns>
        public static IClientBuilder UseTransactions(this IClientBuilder builder)
            => builder.ConfigureServices(services => services.UseTransactionsWithClient());
    }
}
