using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Runtime;
using Orleans.Transactions.Abstractions;
using Orleans.Transactions.DynamoDB.TransactionalState;

namespace Orleans.Transactions.TestKit.Base.FaultInjection.ControlledInjection;

/// <summary>
/// 圆环 DynamoDB 国家交易存储,并注入交易过失。
/// </summary>
/// <typeparam name="TState">状态类型。</typeparam>
public class FaultInjectionDynamoDBTransactionStateStorage<TState> : ITransactionalStateStorage<TState>
    where TState : class, new()
{
    private readonly DynamoDBTransactionalStateStorage<TState> stateStorage;
    private readonly ITransactionFaultInjector faultInjector;

    /// <summary>
    /// 初始化 <see cref="FaultInjectionDynamoDBTransactionStateStorage{TState}"/> 类的新实例。
    /// </summary>
    /// <param name="faultInjector">交易错误注射器。</param>
    /// <param name="dynamodbStateStorage">包裹 DynamoDB 国家交易储存。</param>
    public FaultInjectionDynamoDBTransactionStateStorage(
        ITransactionFaultInjector faultInjector,
        DynamoDBTransactionalStateStorage<TState> dynamodbStateStorage)
    {
        this.faultInjector = faultInjector;
        this.stateStorage = dynamodbStateStorage;
    }

    /// <inheritdoc />
    public Task<TransactionalStorageLoadResponse<TState>> Load()
    {
        return this.stateStorage.Load();
    }

    /// <inheritdoc />
    public async Task<string> Store(

        string? expectedETag,
        TransactionalStateMetaData metadata,

        // 一份待编制的交易清单。
        List<PendingTransactionState<TState>>? statesToPrepare,

        // 如果非Null, 则在包括该序列号之前并在此序号之内承诺所有待处理的交易。
        long? commitUpTo,

        // 如果非中键,则中止所有序列号严格大于此序列的待决交易。
        long? abortAfter
    )
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var transactionIds = ControlledTransactionFaultInjectorExtensions.GetTransactionIds(
            metadata,
            statesToPrepare);
        faultInjector.BeforeStore(transactionIds);
        var result = await this.stateStorage.Store(expectedETag, metadata, statesToPrepare, commitUpTo, abortAfter);
        faultInjector.AfterStore(transactionIds);
        return result;
    }
}

/// <summary>
/// 创建错误输入 DynamoDB 交易状态存储实例。
/// </summary>
public class FaultInjectionDynamoDBTransactionStateStorageFactory : ITransactionalStateStorageFactory,
    ILifecycleParticipant<ISiloLifecycle>
{
    private readonly DynamoDBTransactionalStateStorageFactory factory;

    /// <summary>
    /// 创建了错误注射交易性国家储存工厂
    /// </summary>
    /// <param name="services">服务供应商。</param>
    /// <param name="name">提供者名称。</param>
    /// <returns>交易性国家储藏厂</returns>
    public static ITransactionalStateStorageFactory Create(IServiceProvider services, string name)
    {
        var optionsMonitor = services.GetRequiredService<IOptionsMonitor<DynamoDBTransactionalStorageOptions>>();
        var dynamodbFactory = ActivatorUtilities.CreateInstance<DynamoDBTransactionalStateStorageFactory>(services, name, optionsMonitor.Get(name));
        return new FaultInjectionDynamoDBTransactionStateStorageFactory(dynamodbFactory);
    }

    /// <summary>
    /// 初始化 <see cref="FaultInjectionDynamoDBTransactionStateStorageFactory"/> 类的新实例。
    /// </summary>
    /// <param name="factory">包裹 DynamoDB 国家交易储存厂</param>
    public FaultInjectionDynamoDBTransactionStateStorageFactory(
        DynamoDBTransactionalStateStorageFactory factory)
    {
        this.factory = factory;
    }

    /// <inheritdoc />
    [SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "Orleans invokes transactional state storage factories with the current non-null grain context.")]
    public ITransactionalStateStorage<TState> Create<TState>(string stateName, IGrainContext context) where TState : class, new()
    {
        var dynamodbStateStorage = this.factory.Create<TState>(stateName, context);
        return ActivatorUtilities.CreateInstance<FaultInjectionDynamoDBTransactionStateStorage<TState>>(
            context.ActivationServices, dynamodbStateStorage);
    }

    /// <inheritdoc />
    public void Participate(ISiloLifecycle lifecycle)
    {
        this.factory.Participate(lifecycle);
    }
}
