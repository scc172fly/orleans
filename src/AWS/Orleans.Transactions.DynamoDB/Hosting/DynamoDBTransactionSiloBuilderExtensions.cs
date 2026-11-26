using System;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Providers;

namespace Orleans.Hosting;

/// <summary>
/// 配置 DynamoDB 交易状态储存的扩展。
/// </summary>
public static class DynamoDBTransactionSiloBuilderExtensions
{
    /// <summary>
    /// 配置 silo 将 DynamoDB 存储用作默认交易 grain 存储。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="configureOptions">配置提供者所用的动作。</param>
    /// <returns>下 silo 建筑商</returns>
    public static ISiloBuilder AddDynamoDBTransactionalStateStorageAsDefault(this ISiloBuilder builder, Action<DynamoDBTransactionalStorageOptions> configureOptions)
    {
        return builder.AddDynamoDBTransactionalStateStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
    }

    /// <summary>
    /// 配置配置 silo 使用 DynamoDB 用于交易存储 grain 储存。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="name">提供者名称。</param>
    /// <param name="configureOptions">配置提供者所用的动作。</param>
    /// <returns>下 silo 建筑商</returns>
    public static ISiloBuilder AddDynamoDBTransactionalStateStorage(this ISiloBuilder builder, string name, Action<DynamoDBTransactionalStorageOptions> configureOptions)
    {
        return builder.ConfigureServices(services => services.AddDynamoDBTransactionalStateStorage(name, ob => ob.Configure(configureOptions)));
    }

    /// <summary>
    /// 配置 silo 将 DynamoDB 存储用作默认交易 grain 存储。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="configureOptions">配置提供者所用的动作。</param>
    /// <returns>下 silo 建筑商</returns>
    public static ISiloBuilder AddDynamoDBTransactionalStateStorageAsDefault(this ISiloBuilder builder, Action<OptionsBuilder<DynamoDBTransactionalStorageOptions>>? configureOptions = null)
    {
        return builder.AddDynamoDBTransactionalStateStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
    }

    /// <summary>
    /// 配置配置 silo 使用 DynamoDB 用于交易存储 grain 储存。
    /// </summary>
    /// <param name="builder">下 silo 建筑商</param>
    /// <param name="name">提供者名称。</param>
    /// <param name="configureOptions">配置提供者所用的动作。</param>
    /// <returns>下 silo 建筑商</returns>
    public static ISiloBuilder AddDynamoDBTransactionalStateStorage(this ISiloBuilder builder, string name, Action<OptionsBuilder<DynamoDBTransactionalStorageOptions>>? configureOptions = null)
    {
        return builder.ConfigureServices(services => services.AddDynamoDBTransactionalStateStorage(name, configureOptions));
    }
}
