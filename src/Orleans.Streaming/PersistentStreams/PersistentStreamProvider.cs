using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Internal;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Streams;
using Orleans.Streams.Core;

namespace Orleans.Providers.Streams.Common
{
    /// <summary>
    /// <see cref="PersistentStreamProvider" /> 可以处理的命令。
    /// </summary>
    /// <seealso cref="IControllable"/>
    [Serializable]
    public enum PersistentStreamProviderCommand
    {
        /// <summary>
        /// 无命令。
        /// </summary>
        None,

        /// <summary>
        /// 启动代理。
        /// </summary>
        StartAgents,

        /// <summary>
        /// 停止代理。
        /// </summary>
        StopAgents,

        /// <summary>
        /// 获取代理状态。
        /// </summary>
        GetAgentsState,

        /// <summary>
        /// 获取正在运行的代理数量。
        /// </summary>
        GetNumberRunningAgents,

        /// <summary>
        /// 自定义适配器命令范围的起始值。
        /// </summary>
        AdapterCommandStartRange = 10000,

        /// <summary>
        /// 自定义适配器命令范围的结束值。
        /// </summary>
        AdapterCommandEndRange = AdapterCommandStartRange + 9999,

        /// <summary>
        /// 自定义适配器工厂命令范围的起始值。
        /// </summary>
        AdapterFactoryCommandStartRange = AdapterCommandEndRange + 1,

        /// <summary>
        /// 自定义适配器工厂命令范围的结束值。
        /// </summary>
        AdapterFactoryCommandEndRange = AdapterFactoryCommandStartRange + 9999,
    }

    /// <summary>
    /// 使用适配器进行持久化的持久化流提供程序。
    /// </summary>
    public partial class PersistentStreamProvider : IStreamProvider, IInternalStreamProvider, IControllable, IStreamSubscriptionManagerRetriever, ILifecycleParticipant<ILifecycleObservable>
    {
        private readonly ILogger logger;
        private readonly IStreamProviderRuntime runtime;
        private readonly DeepCopier deepCopier;
        private readonly IRuntimeClient runtimeClient;
        private readonly ProviderStateManager stateManager = new ProviderStateManager();
        private IQueueAdapterFactory adapterFactory = null!;
        private IQueueAdapter queueAdapter = null!;
        private IPersistentStreamPullingManager? pullingAgentManager;
        private IStreamSubscriptionManager? streamSubscriptionManager;
        private readonly StreamPubSubOptions pubsubOptions;
        private readonly StreamLifecycleOptions lifeCycleOptions;

        /// <inheritdoc />
        public string Name { get; private set; }

        /// <inheritdoc />
        public bool IsRewindable { get { return queueAdapter.IsRewindable; } }

        /// <summary>
        /// 初始化 <see cref="PersistentStreamProvider"/> 类的新实例。
        /// </summary>
        /// <param name="name">流提供程序名称。</param>
        /// <param name="pubsubOptions">流发布/订阅选项。</param>
        /// <param name="lifeCycleOptions">流生命周期选项。</param>
        /// <param name="runtime">提供程序运行时。</param>
        /// <param name="deepCopier">深拷贝器。</param>
        /// <param name="logger">日志记录器。</param>
        public PersistentStreamProvider(
            string name,
            StreamPubSubOptions pubsubOptions,
            StreamLifecycleOptions lifeCycleOptions,
            IProviderRuntime runtime,
            DeepCopier deepCopier,
            ILogger<PersistentStreamProvider> logger)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            this.pubsubOptions = pubsubOptions ?? throw new ArgumentNullException(nameof(pubsubOptions));
            this.Name = name;
            this.lifeCycleOptions = lifeCycleOptions ?? throw new ArgumentNullException(nameof(lifeCycleOptions));
            this.runtime = runtime.ServiceProvider.GetRequiredService<IStreamProviderRuntime>();
            this.runtimeClient = runtime.ServiceProvider.GetRequiredService<IRuntimeClient>();
            this.deepCopier = deepCopier ?? throw new ArgumentNullException(nameof(deepCopier));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        private async Task Init(CancellationToken token)
        {
            if (!this.stateManager.PresetState(ProviderState.Initialized)) return;
            this.adapterFactory = this.runtime.ServiceProvider.GetRequiredKeyedService<IQueueAdapterFactory>(this.Name);
            this.queueAdapter = await adapterFactory.CreateAdapter(token);

            if (this.pubsubOptions.PubSubType == StreamPubSubType.ExplicitGrainBasedAndImplicit
                || this.pubsubOptions.PubSubType == StreamPubSubType.ExplicitGrainBasedOnly)
            {
                var subscriptionManagerAdmin = this.runtime.ServiceProvider.GetService<IStreamSubscriptionManagerAdmin>()!;
                this.streamSubscriptionManager = subscriptionManagerAdmin.GetStreamSubscriptionManager(StreamSubscriptionManagerType.ExplicitSubscribeOnly);
            }
            this.stateManager.CommitState();
        }

        private async Task Start(CancellationToken token)
        {
            if (!this.stateManager.PresetState(ProviderState.Started)) return;
            if (this.queueAdapter.Direction.Equals(StreamProviderDirection.ReadOnly) ||
                this.queueAdapter.Direction.Equals(StreamProviderDirection.ReadWrite))
            {
                var siloRuntime = this.runtime as ISiloSideStreamProviderRuntime;
                if (siloRuntime != null)
                {
                    this.pullingAgentManager = await siloRuntime.InitializePullingAgents(this.Name, this.adapterFactory, this.queueAdapter, token);

                    // TODO：尚不支持 DeliveryDisabled，仅支持 Stopped 和 Started。
                    if (this.lifeCycleOptions.StartupState == StreamLifecycleOptions.RunState.AgentsStarted)
                        await pullingAgentManager.StartAgents(token);
                }
            }
            stateManager.CommitState();
        }

        /// <inheritdoc />
        public IStreamSubscriptionManager? GetStreamSubscriptionManager()
        {
            return this.streamSubscriptionManager;
        }

        private async Task Close(CancellationToken token)
        {
            if (!stateManager.PresetState(ProviderState.Closed)) return;

            var manager = this.pullingAgentManager;
            if (manager != null)
            {
                var stopTask = manager.Stop(CancellationToken.None);
                try
                {
                    await stopTask.WaitAsync(token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    // 生命周期截止时间仅限制观察者的等待；管理器的清理工作仍会继续。
                    stopTask.Ignore();
                }
            }

            stateManager.CommitState();
        }

        /// <inheritdoc />
        public IAsyncStream<T> GetStream<T>(StreamId streamId)
        {
            var id = new QualifiedStreamId(Name, streamId);
            return this.runtime.GetStreamDirectory().GetOrAddStream<T>(
                id, () => new StreamImpl<T>(id, this, IsRewindable, this.runtimeClient));
        }

        IInternalAsyncBatchObserver<T> IInternalStreamProvider.GetProducerInterface<T>(IAsyncStream<T> stream)
        {
            if (queueAdapter.Direction == StreamProviderDirection.ReadOnly)
            {
                throw new InvalidOperationException($"Stream provider {queueAdapter.Name} is ReadOnly.");
            }
            return new PersistentStreamProducer<T>((StreamImpl<T>)stream, this.runtime, queueAdapter, IsRewindable, this.deepCopier);
        }

        IInternalAsyncObservable<T> IInternalStreamProvider.GetConsumerInterface<T>(IAsyncStream<T> streamId)
        {
            return GetConsumerInterfaceImpl(streamId);
        }

        private IInternalAsyncObservable<T> GetConsumerInterfaceImpl<T>(IAsyncStream<T> stream)
        {
            return new StreamConsumer<T>((StreamImpl<T>)stream, Name, this.runtime, this.runtime.PubSub(this.pubsubOptions.PubSubType)!, this.logger, IsRewindable); // 已配置的 StreamPubSubType 值始终会选定一个运行时。
        }

        /// <inheritdoc />
        public Task<object?> ExecuteCommand(int command, object? arg)
        {
            if (command >= (int)PersistentStreamProviderCommand.AdapterCommandStartRange &&
                command <= (int)PersistentStreamProviderCommand.AdapterCommandEndRange &&
                queueAdapter is IControllable)
            {
                return ((IControllable)queueAdapter).ExecuteCommand(command, arg);
            }

            if (command >= (int)PersistentStreamProviderCommand.AdapterFactoryCommandStartRange &&
                command <= (int)PersistentStreamProviderCommand.AdapterFactoryCommandEndRange &&
                adapterFactory is IControllable)
            {
                return ((IControllable)adapterFactory).ExecuteCommand(command, arg);
            }

            if (pullingAgentManager != null)
            {
                return pullingAgentManager.ExecuteCommand((PersistentStreamProviderCommand)command, arg);
            }

            LogWarningGotCommand(
                (PersistentStreamProviderCommand)command,
                arg);
            throw new ArgumentException("PullingAgentManager is not initialized yet.");
        }

        /// <inheritdoc />
        public void Participate(ILifecycleObservable lifecycle)
        {
            lifecycle.Subscribe(OptionFormattingUtilities.Name<PersistentStreamProvider>(this.Name), this.lifeCycleOptions.InitStage, Init);
            lifecycle.Subscribe(OptionFormattingUtilities.Name<PersistentStreamProvider>(this.Name), this.lifeCycleOptions.StartStage, Start, Close);
        }

        /// <summary>
        /// 创建持久化流提供程序。
        /// </summary>
        /// <param name="services">服务提供程序。</param>
        /// <param name="name">流提供程序名称。</param>
        /// <returns>持久化流提供程序。</returns>
        public static IStreamProvider Create(IServiceProvider services, string name)
        {
            var pubsubOptions = services.GetRequiredService<IOptionsMonitor<StreamPubSubOptions>>().Get(name);
            var initOptions = services.GetRequiredService<IOptionsMonitor<StreamLifecycleOptions>>().Get(name);
            return ActivatorUtilities.CreateInstance<PersistentStreamProvider>(services, name, pubsubOptions, initOptions);
        }

        /// <summary>
        /// 获取命名的持久化流提供程序，使其作为指定生命周期的参与者。
        /// </summary>
        /// <typeparam name="TLifecycle">生命周期类型。</typeparam>
        /// <param name="serviceProvider">服务提供程序。</param>
        /// <param name="name">流提供程序名称。</param>
        /// <returns>命名的流提供程序对应的生命周期参与者。</returns>
        public static ILifecycleParticipant<TLifecycle> ParticipateIn<TLifecycle>(IServiceProvider serviceProvider, string name)
            where TLifecycle : ILifecycleObservable
        {
            var provider = (PersistentStreamProvider)serviceProvider.GetRequiredKeyedService<IStreamProvider>(name);
            return provider.ParticipateIn<TLifecycle>();
        }

        [LoggerMessage(
            Level = LogLevel.Warning,
            Message = "Got command {Command} with arg {Argument}, but PullingAgentManager is not initialized yet. Ignoring the command."
        )]
        private partial void LogWarningGotCommand(PersistentStreamProviderCommand command, object? argument);
    }
}
