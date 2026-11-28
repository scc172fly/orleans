using System;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Providers;

namespace Orleans.Hosting;

/// <summary>
/// 用于配置 DynamoDB 事务状态存储的扩展方法。
/// </summary>
public static class DynamoDBTransactionSiloBuilderExtensions
{
    /// <summary>
    /// 配置 Silo 使用 DynamoDB 存储作为默认事务 Grain 存储。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="configureOptions">用于配置提供程序的委托。</param>
    /// <returns>Silo 构建器。</returns>
    public static ISiloBuilder AddDynamoDBTransactionalStateStorageAsDefault(this ISiloBuilder builder, Action<DynamoDBTransactionalStorageOptions> configureOptions)
    {
        return builder.AddDynamoDBTransactionalStateStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
    }

    /// <summary>
    /// 配置 Silo 使用 DynamoDB 存储作为事务 Grain 存储。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="name">提供程序名称。</param>
    /// <param name="configureOptions">用于配置提供程序的委托。</param>
    /// <returns>Silo 构建器。</returns>
    public static ISiloBuilder AddDynamoDBTransactionalStateStorage(this ISiloBuilder builder, string name, Action<DynamoDBTransactionalStorageOptions> configureOptions)
    {
        return builder.ConfigureServices(services => services.AddDynamoDBTransactionalStateStorage(name, ob => ob.Configure(configureOptions)));
    }

    /// <summary>
    /// 配置 Silo 使用 DynamoDB 存储作为默认事务 Grain 存储。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="configureOptions">用于配置提供程序的委托。</param>
    /// <returns>Silo 构建器。</returns>
    public static ISiloBuilder AddDynamoDBTransactionalStateStorageAsDefault(this ISiloBuilder builder, Action<OptionsBuilder<DynamoDBTransactionalStorageOptions>>? configureOptions = null)
    {
        return builder.AddDynamoDBTransactionalStateStorage(ProviderConstants.DEFAULT_STORAGE_PROVIDER_NAME, configureOptions);
    }

    /// <summary>
    /// 配置 Silo 使用 DynamoDB 存储作为事务 Grain 存储。
    /// </summary>
    /// <param name="builder">Silo 构建器。</param>
    /// <param name="name">提供程序名称。</param>
    /// <param name="configureOptions">用于配置提供程序的委托。</param>
    /// <returns>Silo 构建器。</returns>
    public static ISiloBuilder AddDynamoDBTransactionalStateStorage(this ISiloBuilder builder, string name, Action<OptionsBuilder<DynamoDBTransactionalStorageOptions>>? configureOptions = null)
    {
        return builder.ConfigureServices(services => services.AddDynamoDBTransactionalStateStorage(name, configureOptions));
    }
}
