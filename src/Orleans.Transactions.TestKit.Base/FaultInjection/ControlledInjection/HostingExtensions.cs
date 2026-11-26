using System;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Providers;
using Orleans.Transactions.TestKit;

namespace Orleans.Transactions.TestKit;

/// <summary>
/// 配置交易过失注入的扩展。
/// </summary>
public static class SiloBuilderExtensions
{
    /// <summary>
    /// 配置组群集, 使用分布式TM算法
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <returns>下 silo 建筑商</returns>
    public static ISiloBuilder UseControlledFaultInjectionTransactionState(this ISiloBuilder builder)
    {
        return builder.ConfigureServices(services => services.UseControlledFaultInjectionTransactionState());
    }

    /// <summary>
    /// 配置错误输入 Azure 表格交易状态存储作为默认提供者。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="configureOptions">配置提供者所用的动作。</param>
    /// <returns>下 silo 建筑商</returns>
    public static ISiloBuilder AddFaultInjectionAzureTableTransactionalStateStorage(this ISiloBuilder builder, Action<AzureTableTransactionalStateOptions> configureOptions)
    {
        return builder.AddFaultInjectionAzureTableTransactionalStateStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
    }

    /// <summary>
    /// 配置错误输入 Azure 表格交易状态存储。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="name">提供者名称。</param>
    /// <param name="configureOptions">配置提供者所用的动作。</param>
    /// <returns>下 silo 建筑商</returns>
    public static ISiloBuilder AddFaultInjectionAzureTableTransactionalStateStorage(this ISiloBuilder builder, string name, Action<AzureTableTransactionalStateOptions> configureOptions)
    {
        return builder.ConfigureServices(services => services.AddFaultInjectionAzureTableTransactionalStateStorage(name, ob => ob.Configure(configureOptions)));
    }

    /// <summary>
    /// 配置错误输入 DynamoDB 交易状态存储 。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="name">提供者名称。</param>
    /// <param name="configureOptions">配置提供者所用的动作。</param>
    /// <returns>下 silo 建筑商</returns>
    public static ISiloBuilder AddFaultInjectionDynamoDBTransactionalStateStorage(this ISiloBuilder builder, string name, Action<DynamoDBTransactionalStorageOptions> configureOptions)
    {
        return builder.ConfigureServices(services => services.AddFaultInjectionDynamoDBTransactionalStateStorage(name, ob => ob.Configure(configureOptions)));
    }
}
