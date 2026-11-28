using System;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Providers;
using Orleans.Transactions.TestKit;

namespace Orleans.Transactions.TestKit;

/// <summary>
/// 用于配置事务故障注入的扩展方法。
/// </summary>
public static class SiloBuilderExtensions
{
    /// <summary>
    /// 配置集群以使用分布式事务管理器算法
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <returns>Silo 构建器。</returns>
    public static ISiloBuilder UseControlledFaultInjectionTransactionState(this ISiloBuilder builder)
    {
        return builder.ConfigureServices(services => services.UseControlledFaultInjectionTransactionState());
    }

    /// <summary>
    /// 将支持故障注入的 Azure Table 事务状态存储配置为默认提供程序。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="configureOptions">用于配置提供程序的委托。</param>
    /// <returns>Silo 构建器。</returns>
    public static ISiloBuilder AddFaultInjectionAzureTableTransactionalStateStorage(this ISiloBuilder builder, Action<AzureTableTransactionalStateOptions> configureOptions)
    {
        return builder.AddFaultInjectionAzureTableTransactionalStateStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
    }

    /// <summary>
    /// 配置支持故障注入的 Azure Table 事务状态存储。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="name">提供程序名称。</param>
    /// <param name="configureOptions">用于配置提供程序的委托。</param>
    /// <returns>Silo 构建器。</returns>
    public static ISiloBuilder AddFaultInjectionAzureTableTransactionalStateStorage(this ISiloBuilder builder, string name, Action<AzureTableTransactionalStateOptions> configureOptions)
    {
        return builder.ConfigureServices(services => services.AddFaultInjectionAzureTableTransactionalStateStorage(name, ob => ob.Configure(configureOptions)));
    }

    /// <summary>
    /// 配置支持故障注入的 DynamoDB 事务状态存储。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="name">提供程序名称。</param>
    /// <param name="configureOptions">用于配置提供程序的委托。</param>
    /// <returns>Silo 构建器。</returns>
    public static ISiloBuilder AddFaultInjectionDynamoDBTransactionalStateStorage(this ISiloBuilder builder, string name, Action<DynamoDBTransactionalStorageOptions> configureOptions)
    {
        return builder.ConfigureServices(services => services.AddFaultInjectionDynamoDBTransactionalStateStorage(name, ob => ob.Configure(configureOptions)));
    }
}
