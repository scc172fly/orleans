using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Runtime;
using Orleans.Storage;
using Orleans.Transactions.Abstractions;
#if CLUSTERING_DYNAMODB
using Orleans.Clustering.DynamoDB;
#elif PERSISTENCE_DYNAMODB
using Orleans.Persistence.DynamoDB;
#elif REMINDERS_DYNAMODB
using Orleans.Reminders.DynamoDB;
#elif AWSUTILS_TESTS
using Orleans.AWSUtils.Tests;
#elif TRANSACTIONS_DYNAMODB
using Orleans.Transactions.DynamoDB;
#else
#endif

namespace Orleans.Transactions.DynamoDB.TransactionalState;

/// <summary>
/// 创建由 DynamoDB 支持的事务状态存储实例。
/// </summary>
public partial class DynamoDBTransactionalStateStorageFactory : ITransactionalStateStorageFactory, ILifecycleParticipant<ISiloLifecycle>
{
    private readonly string name;
    private readonly DynamoDBTransactionalStorageOptions options;
    private readonly ClusterOptions clusterOptions;
    private readonly ILoggerFactory loggerFactory;

    private DynamoDBStorage storage = null!;

    /// <summary>
    /// 初始化 <see cref="DynamoDBTransactionalStateStorageFactory"/> 类的新实例。
    /// </summary>
    /// <param name="name">提供程序名称。</param>
    /// <param name="options">提供程序选项。</param>
    /// <param name="clusterOptions">集群选项。</param>
    /// <param name="services">服务提供程序。</param>
    /// <param name="loggerFactory">日志记录器工厂。</param>
    public DynamoDBTransactionalStateStorageFactory(
        string name,
        DynamoDBTransactionalStorageOptions options,
        IOptions<ClusterOptions> clusterOptions,
        IServiceProvider services,
        ILoggerFactory loggerFactory)
    {
        this.name = name;
        this.options = options;
        this.clusterOptions = clusterOptions.Value;
        this.loggerFactory = loggerFactory;
    }

    /// <summary>
    /// 创建事务状态存储工厂。
    /// </summary>
    /// <param name="services">服务提供程序。</param>
    /// <param name="name">提供程序名称。</param>
    /// <returns>事务状态存储工厂。</returns>
    public static ITransactionalStateStorageFactory Create(IServiceProvider services, string name)
    {
        var optionsMonitor = services.GetRequiredService<IOptionsMonitor<DynamoDBTransactionalStorageOptions>>();
        return ActivatorUtilities.CreateInstance<DynamoDBTransactionalStateStorageFactory>(services, name, optionsMonitor.Get(name));
    }

    /// <inheritdoc />
    public ITransactionalStateStorage<TState> Create<TState>(string stateName, IGrainContext context) where TState : class, new()
    {
        if (this.storage is null)
        {
            throw new ArgumentException("DynamoDBStorage client is not initialized");
        }

        var partitionKey = this.MakePartitionKey(context, stateName);
        var logger = this.loggerFactory.CreateLogger<DynamoDBTransactionalStateStorage<TState>>();
        return ActivatorUtilities.CreateInstance<DynamoDBTransactionalStateStorage<TState>>(context.ActivationServices, this.storage, this.options, partitionKey, logger);
    }

    /// <inheritdoc />
    public void Participate(ISiloLifecycle lifecycle)
    {
        lifecycle.Subscribe(OptionFormattingUtilities.Name<DynamoDBTransactionalStateStorageFactory>(this.name), this.options.InitStage, Init);
    }

    private async Task Initialize(CancellationToken cancellationToken)
    {
        var stopWatch = Stopwatch.StartNew();
        var logger = this.loggerFactory.CreateLogger<DynamoDBStorage>();

        try
        {
            var initMsg = string.Format(CultureInfo.CurrentCulture, "Init: Name={0} ServiceId={1} Table={2}", this.name, this.options.ServiceId, this.options.TableName);

            LogInformationInitializingDynamoDBGrainStorage(logger, this.name, initMsg);

            this.storage = new DynamoDBStorage(
                logger,
                this.options.Service,
                this.options.AccessKey,
                this.options.SecretKey,
                this.options.Token,
                this.options.ProfileName,
                this.options.ReadCapacityUnits,
                this.options.WriteCapacityUnits,
                this.options.UseProvisionedThroughput,
                this.options.CreateIfNotExists,
                this.options.UpdateIfExists);

            await storage.InitializeTable(this.options.TableName,
                new List<KeySchemaElement>
                {
                    new KeySchemaElement { AttributeName = DynamoDBTransactionalStateConstants.PARTITION_KEY_PROPERTY_NAME, KeyType = KeyType.HASH },
                    new KeySchemaElement { AttributeName = DynamoDBTransactionalStateConstants.ROW_KEY_PROPERTY_NAME, KeyType = KeyType.RANGE }
                },
                new List<AttributeDefinition>
                {
                    new AttributeDefinition { AttributeName = DynamoDBTransactionalStateConstants.PARTITION_KEY_PROPERTY_NAME, AttributeType = ScalarAttributeType.S },
                    new AttributeDefinition { AttributeName = DynamoDBTransactionalStateConstants.ROW_KEY_PROPERTY_NAME, AttributeType = ScalarAttributeType.S }
                },
                secondaryIndexes: null,
                ttlAttributeName: null,
                cancellationToken: cancellationToken);

            stopWatch.Stop();
            LogInformationProviderInitialized(logger, this.name, this.GetType().Name, this.options.InitStage, stopWatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exc)
        {
            stopWatch.Stop();
            LogErrorProviderInitFailed(logger, this.name, this.GetType().Name, this.options.InitStage, stopWatch.ElapsedMilliseconds, exc);
            throw;
        }
    }

    private string MakePartitionKey(IGrainContext context, string stateName)
    {
        var grainKey = context.GrainReference.GrainId.ToString();
        return $"{grainKey}_{this.clusterOptions.ServiceId}_{stateName}";
    }

    private Task Init(CancellationToken cancellationToken)
    {
        return Initialize(cancellationToken);
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "AWS DynamoDB Transactional Grain Storage {Name} is initializing: {InitMsg}"
    )]
    private static partial void LogInformationInitializingDynamoDBGrainStorage(ILogger logger, string name, string initMsg);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Initializing provider {Name} of type {Type} in stage {Stage} took {ElapsedMilliseconds} Milliseconds."
    )]
    private static partial void LogInformationProviderInitialized(ILogger logger, string name, string type, int stage, long elapsedMilliseconds);

    [LoggerMessage(
        EventId = (int)ErrorCode.Provider_ErrorFromInit,
        Level = LogLevel.Error,
        Message = "Initialization failed for provider {Name} of type {Type} in stage {Stage} in {ElapsedMilliseconds} Milliseconds."
    )]
    private static partial void LogErrorProviderInitFailed(ILogger logger, string name, string type, int stage, long elapsedMilliseconds, Exception exception);
}
