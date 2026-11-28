using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Azure.Data.Tables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Orleans.Configuration;
using Orleans.Runtime;
using Orleans.Transactions.Abstractions;

namespace Orleans.Transactions.AzureStorage
{
    /// <summary>
    /// 创建以 Azure Table Storage 为后端的事务状态存储实例。
    /// </summary>
    public class AzureTableTransactionalStateStorageFactory : ITransactionalStateStorageFactory, ILifecycleParticipant<ISiloLifecycle>
    {
        private readonly string name;
        private readonly AzureTableTransactionalStateOptions options;
        private readonly ClusterOptions clusterOptions;
        private readonly JsonSerializerSettings jsonSettings;
        private readonly ILoggerFactory loggerFactory;
        private TableClient table = null!;

        /// <summary>
        /// 根据已注册的命名选项创建事务状态存储工厂。
        /// </summary>
        /// <param name="services">服务提供程序。</param>
        /// <param name="name">提供程序名称。</param>
        /// <returns>事务状态存储工厂。</returns>
        public static ITransactionalStateStorageFactory Create(IServiceProvider services, string name)
        {
            var optionsMonitor = services.GetRequiredService<IOptionsMonitor<AzureTableTransactionalStateOptions>>();
            return ActivatorUtilities.CreateInstance<AzureTableTransactionalStateStorageFactory>(services, name, optionsMonitor.Get(name));
        }

        /// <summary>
        /// 初始化 <see cref="AzureTableTransactionalStateStorageFactory"/> 类的新实例。
        /// </summary>
        /// <param name="name">提供程序名称。</param>
        /// <param name="options">提供程序选项。</param>
        /// <param name="clusterOptions">集群选项。</param>
        /// <param name="services">服务提供程序。</param>
        /// <param name="loggerFactory">日志记录器工厂。</param>
        [SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "ActivatorUtilities resolves the non-null IOptions<ClusterOptions> instance from dependency injection.")]
        public AzureTableTransactionalStateStorageFactory(string name, AzureTableTransactionalStateOptions options, IOptions<ClusterOptions> clusterOptions, IServiceProvider services, ILoggerFactory loggerFactory)
        {
            this.name = name;
            this.options = options;
            this.clusterOptions = clusterOptions.Value;
            this.jsonSettings = TransactionalStateFactory.GetJsonSerializerSettings(services);
            this.loggerFactory = loggerFactory;
        }

        /// <inheritdoc />
        [SuppressMessage("Design", "CA1062:Validate arguments of public methods", Justification = "Orleans invokes transactional state storage factories with the current non-null grain context.")]
        public ITransactionalStateStorage<TState> Create<TState>(string stateName, IGrainContext context) where TState : class, new()
        {
            string partitionKey = MakePartitionKey(context, stateName);
            return ActivatorUtilities.CreateInstance<AzureTableTransactionalStateStorage<TState>>(context.ActivationServices, this.table, partitionKey, this.jsonSettings);
        }

        /// <inheritdoc />
        public void Participate(ISiloLifecycle lifecycle)
        {
            lifecycle.Subscribe(OptionFormattingUtilities.Name<AzureTableTransactionalStateStorageFactory>(this.name), this.options.InitStage, Init);
        }

        private string MakePartitionKey(IGrainContext context, string stateName)
        {
            string grainKey = context.GrainReference.GrainId.ToString();
            var key = $"{grainKey}_{this.clusterOptions.ServiceId}_{stateName}";
            return AzureTableUtils.SanitizeTableProperty(key);
        }

        private async Task CreateTable(CancellationToken cancellationToken)
        {
            var tableManager = new AzureTableDataManager<TableEntity>(
                this.options,
                this.loggerFactory.CreateLogger<AzureTableDataManager<TableEntity>>());
            await tableManager.InitTableAsync(cancellationToken);
            this.table = tableManager.Table;
        }

        private Task Init(CancellationToken cancellationToken)
        {
            return CreateTable(cancellationToken);
        }
    }
}
