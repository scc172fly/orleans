using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Orleans.Runtime;
using Orleans.Transactions.Abstractions;
using Orleans.Transactions.AzureStorage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Orleans.Configuration;

namespace Orleans.Transactions.TestKit
{
    /// <summary>
    /// 圆环 Azure 表格交易国家储存,并注入交易过失。
    /// </summary>
    /// <typeparam name="TState">状态类型。</typeparam>
    public class FaultInjectionAzureTableTransactionStateStorage<TState> : ITransactionalStateStorage<TState>
        where TState : class, new()
    {
        private readonly AzureTableTransactionalStateStorage<TState> stateStorage;
        private readonly ITransactionFaultInjector faultInjector;
        /// <summary>
        /// 初始化 <see cref="FaultInjectionAzureTableTransactionStateStorage{TState}"/> 类的新实例。
        /// </summary>
        /// <param name="faultInjector">交易错误注射器。</param>
        /// <param name="azureStateStorage">包裹 Azure 国家交易储存表。</param>
        public FaultInjectionAzureTableTransactionStateStorage(ITransactionFaultInjector faultInjector,
            AzureTableTransactionalStateStorage<TState> azureStateStorage)
        {
            this.faultInjector = faultInjector;
            this.stateStorage = azureStateStorage;
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
    /// 创建错误输入 Azure 表格交易状态存储实例。
    /// </summary>
    public class FaultInjectionAzureTableTransactionStateStorageFactory : ITransactionalStateStorageFactory,
        ILifecycleParticipant<ISiloLifecycle>
    {
        private readonly AzureTableTransactionalStateStorageFactory factory;

        /// <summary>
        /// 创建了错误注射交易性国家储存工厂
        /// </summary>
        /// <param name="services">服务供应商。</param>
        /// <param name="name">提供者名称。</param>
        /// <returns>交易性国家储藏厂</returns>
        public static ITransactionalStateStorageFactory Create(IServiceProvider services, string name)
        {
            var optionsMonitor = services.GetRequiredService<IOptionsMonitor<AzureTableTransactionalStateOptions>>();
            var azureFactory = ActivatorUtilities.CreateInstance<AzureTableTransactionalStateStorageFactory>(services, name, optionsMonitor.Get(name));
            return new FaultInjectionAzureTableTransactionStateStorageFactory(azureFactory);
        }

        /// <summary>
        /// 初始化 <see cref="FaultInjectionAzureTableTransactionStateStorageFactory"/> 类的新实例。
        /// </summary>
        /// <param name="factory">包裹 Azure 国家交易储存工厂</param>
        public FaultInjectionAzureTableTransactionStateStorageFactory(
            AzureTableTransactionalStateStorageFactory factory)
        {
            this.factory = factory;
        }

        /// <inheritdoc />
        [SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "Orleans invokes transactional state storage factories with the current non-null grain context.")]
        public ITransactionalStateStorage<TState> Create<TState>(string stateName, IGrainContext context) where TState : class, new()
        {
            var azureStateStorage = this.factory.Create<TState>(stateName, context);
            return ActivatorUtilities.CreateInstance<FaultInjectionAzureTableTransactionStateStorage<TState>>(
                context.ActivationServices, azureStateStorage);
        }

        /// <inheritdoc />
        public void Participate(ISiloLifecycle lifecycle)
        {
            this.factory.Participate(lifecycle);
        }
    }
}
