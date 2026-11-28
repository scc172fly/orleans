using Orleans.Transactions;
using Orleans.Transactions.Abstractions;

namespace Orleans.Hosting;

/// <summary>
/// 提供针对 <see cref="ISiloBuilder"/> 的事务配置扩展方法。
/// </summary>
public static class SiloBuilderExtensions
{
    /// <summary>
    /// 向 Silo 添加 Orleans 事务服务和事务协议 Grain 扩展。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <returns>Silo 构建器。</returns>
    public static ISiloBuilder UseTransactions(this ISiloBuilder builder)
    {
        return builder.ConfigureServices(services => services.UseTransactionsWithSilo())
                      .AddGrainExtension<ITransactionManagerExtension, TransactionManagerExtension>()
                      .AddGrainExtension<ITransactionalResourceExtension, TransactionalResourceExtension>();
    }
}
