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
/// 包装 DynamoDB 事务状态存储，以注入事务故障。
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
    /// <param name="faultInjector">事务故障注入器。</param>
    /// <param name="dynamodbStateStorage">被包装的 DynamoDB 事务状态存储。</param>
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

        // 要准备的事务列表。
        List<PendingTransactionState<TState>>? statesToPrepare,

        // 若非 null，则提交序列号小于或等于此值的所有待处理事务。
        long? commitUpTo,

        // 若非 null，则中止序列号严格大于此值的所有待处理事务。
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
/// 创建支持故障注入的 DynamoDB 事务状态存储实例。
/// </summary>
public class FaultInjectionDynamoDBTransactionStateStorageFactory : ITransactionalStateStorageFactory,
    ILifecycleParticipant<ISiloLifecycle>
{
    private readonly DynamoDBTransactionalStateStorageFactory factory;

    /// <summary>
    /// 创建支持故障注入的事务状态存储工厂。
    /// </summary>
    /// <param name="services">服务提供程序。</param>
    /// <param name="name">提供程序名称。</param>
    /// <returns>事务状态存储工厂。</returns>
    public static ITransactionalStateStorageFactory Create(IServiceProvider services, string name)
    {
        var optionsMonitor = services.GetRequiredService<IOptionsMonitor<DynamoDBTransactionalStorageOptions>>();
        var dynamodbFactory = ActivatorUtilities.CreateInstance<DynamoDBTransactionalStateStorageFactory>(services, name, optionsMonitor.Get(name));
        return new FaultInjectionDynamoDBTransactionStateStorageFactory(dynamodbFactory);
    }

    /// <summary>
    /// 初始化 <see cref="FaultInjectionDynamoDBTransactionStateStorageFactory"/> 类的新实例。
    /// </summary>
    /// <param name="factory">被包装的 DynamoDB 事务状态存储工厂。</param>
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
