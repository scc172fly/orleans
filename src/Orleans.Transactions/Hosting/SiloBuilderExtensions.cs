using Orleans.Transactions;
using Orleans.Transactions.Abstractions;

namespace Orleans.Hosting;

/// <summary>
/// 提供 <see cref="ISiloBuilder"/>的交易配置扩展。
/// </summary>
public static class SiloBuilderExtensions
{
    /// <summary>
    /// 将 Orleans 交易服务和交易协议 grain 延期添加到 silo。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <returns>下 silo 建筑商</returns>
    public static ISiloBuilder UseTransactions(this ISiloBuilder builder)
    {
        return builder.ConfigureServices(services => services.UseTransactionsWithSilo())
                      .AddGrainExtension<ITransactionManagerExtension, TransactionManagerExtension>()
                      .AddGrainExtension<ITransactionalResourceExtension, TransactionalResourceExtension>();
    }
}
