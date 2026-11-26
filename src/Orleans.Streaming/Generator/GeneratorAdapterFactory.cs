using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.Providers.Streams.Common;
using Orleans.Runtime;
using Orleans.Streams;

namespace Orleans.Providers.Streams.Generator
{
    /// <summary>
    /// 流流发电机指令
    /// </summary>
    public enum StreamGeneratorCommand
    {
        /// <summary>
        /// 配置发电机的命令
        /// </summary>
        Configure = PersistentStreamProviderCommand.AdapterFactoryCommandStartRange
    }

    /// <summary>
    /// 河发电机流供应商的适应器工厂
    /// 这个工厂作为适配器和适配装置厂,制造使用可配置发电机的接收器。
    ///   生成事件流,而不是从存储中读取。
    /// </summary>
    public partial class GeneratorAdapterFactory : IQueueAdapterFactory, IQueueAdapter, IQueueAdapterCache, IControllable
    {
        /// <summary>
        /// 发电机配置类型配置的配置属性名称
        /// </summary>
        private readonly HashRingStreamQueueMapperOptions queueMapperOptions;
        private readonly StreamStatisticOptions statisticOptions;
        private readonly IServiceProvider serviceProvider;
        private readonly Serialization.Serializer serializer;
        private readonly ILoggerFactory loggerFactory;
        private readonly ILogger<GeneratorAdapterFactory> logger;
        private readonly OrleansInstruments orleansInstruments;
        private IStreamGeneratorConfig? generatorConfig;
        private IStreamQueueMapper? streamQueueMapper;
        private IStreamFailureHandler? streamFailureHandler;
        private ConcurrentDictionary<QueueId, Receiver> receivers = null!; // 在使用前设置 Init 。
        private IObjectPool<FixedSizeBuffer>? bufferPool;
        private BlockPoolMonitorDimensions blockPoolMonitorDimensions = null!; // 使用前设置 bufferPool 。

        /// <inheritdoc />
        public bool IsRewindable => true;

        /// <inheritdoc />
        public StreamProviderDirection Direction => StreamProviderDirection.ReadOnly;

        /// <inheritdoc />
        public string Name { get; }

        /// <summary>
        /// 创建一个缓存监视器, 以报告缓存相关度
        /// 返回 a ICacheMonitor
        /// </summary>
        protected Func<CacheMonitorDimensions, ICacheMonitor>? CacheMonitorFactory;

        /// <summary>
        /// 创建块块池监测器,以监测块块池相关计量
        /// 返回 a IBlockPoolMonitor
        /// </summary>
        protected Func<BlockPoolMonitorDimensions, IBlockPoolMonitor>? BlockPoolMonitorFactory;

        /// <summary>
        /// 创建监测器以监测 QueueAdapterReceiver 相关计量
        /// 返回 a IQueueAdapterReceiverMonitor
        /// </summary>
        protected Func<ReceiverMonitorDimensions, IQueueAdapterReceiverMonitor>? ReceiverMonitorFactory;

        /// <summary>
        /// 初始化 <see cref="GeneratorAdapterFactory"/> 类的新实例。
        /// </summary>
        /// <param name="providerName">流体提供者名称。</param>
        /// <param name="queueMapperOptions">队列映像仪选项。</param>
        /// <param name="statisticOptions">流统计选项。</param>
        /// <param name="serviceProvider">服务供应商。</param>
        /// <param name="serializer">下 Orleans 连环器</param>
        /// <param name="loggerFactory">伐木厂</param>
        public GeneratorAdapterFactory(
            string providerName,
            HashRingStreamQueueMapperOptions queueMapperOptions,
            StreamStatisticOptions statisticOptions,
            IServiceProvider serviceProvider,
            Serialization.Serializer serializer,
            ILoggerFactory loggerFactory)
        {
            this.Name = providerName;
            this.queueMapperOptions = queueMapperOptions ?? throw new ArgumentNullException(nameof(queueMapperOptions));
            this.statisticOptions = statisticOptions ?? throw new ArgumentNullException(nameof(statisticOptions));
            this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            this.serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            this.loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            this.logger = loggerFactory.CreateLogger<GeneratorAdapterFactory>();
            this.orleansInstruments = serviceProvider.GetRequiredService<OrleansInstruments>();
        }

        /// <summary>
        /// 初始化工厂。
        /// </summary>
        [MemberNotNull(nameof(receivers), nameof(CacheMonitorFactory), nameof(BlockPoolMonitorFactory), nameof(ReceiverMonitorFactory))]
        public void Init()
        {
            this.receivers = new ConcurrentDictionary<QueueId, Receiver>();
            if (CacheMonitorFactory == null)
                this.CacheMonitorFactory = (dimensions) => new DefaultCacheMonitor(dimensions, this.orleansInstruments);
            if (this.BlockPoolMonitorFactory == null)
                this.BlockPoolMonitorFactory = (dimensions) => new DefaultBlockPoolMonitor(dimensions, this.orleansInstruments);
            if (this.ReceiverMonitorFactory == null)
                this.ReceiverMonitorFactory = (dimensions) => new DefaultQueueAdapterReceiverMonitor(dimensions, this.orleansInstruments);
            generatorConfig = this.serviceProvider.GetKeyedService<IStreamGeneratorConfig>(this.Name);
            if (generatorConfig == null)
            {
                LogInfoNoGeneratorConfigurationFound(this.Name);
            }
        }

        [MemberNotNull(nameof(bufferPool))]
        private void CreateBufferPoolIfNotCreatedYet()
        {
            if (this.bufferPool == null)
            {
                // 1兆克区块大小池
                this.blockPoolMonitorDimensions = new BlockPoolMonitorDimensions($"BlockPool-{Guid.NewGuid()}");
                var oneMb = 1 << 20;
                var blockPoolMonitorFactory = this.BlockPoolMonitorFactory!; // Init () 期间设置 。
                var objectPoolMonitor = new ObjectPoolMonitorBridge(blockPoolMonitorFactory(blockPoolMonitorDimensions), oneMb);
                this.bufferPool = new ObjectPool<FixedSizeBuffer>(() => new FixedSizeBuffer(oneMb), objectPoolMonitor, this.statisticOptions.StatisticMonitorWriteInterval);
            }
        }

        /// <inheritdoc />
        public Task<IQueueAdapter> CreateAdapter()
        {
            return Task.FromResult<IQueueAdapter>(this);
        }

        /// <inheritdoc />
        public IQueueAdapterCache GetQueueAdapterCache()
        {
            return this;
        }

        /// <inheritdoc />
        public IStreamQueueMapper GetStreamQueueMapper()
        {
            return streamQueueMapper ?? (streamQueueMapper = new HashRingBasedStreamQueueMapper(this.queueMapperOptions, this.Name));
        }

        /// <inheritdoc />
        public Task<IStreamFailureHandler> GetDeliveryFailureHandler(QueueId queueId)
        {
            return Task.FromResult(streamFailureHandler ?? (streamFailureHandler = new NoOpStreamDeliveryFailureHandler()));
        }

        /// <inheritdoc />
        public Task QueueMessageBatchAsync<T>(StreamId streamId, IEnumerable<T> events, StreamSequenceToken? token,
            Dictionary<string, object>? requestContext)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public IQueueAdapterReceiver CreateReceiver(QueueId queueId)
        {
            if (!receivers.TryGetValue(queueId, out var receiver))
            {
                var dimensions = new ReceiverMonitorDimensions(queueId.ToString());
                var receiverMonitorFactory = this.ReceiverMonitorFactory!; // Init () 期间设置 。
                var receiverMonitor = receiverMonitorFactory(dimensions);
                receiver = receivers.GetOrAdd(queueId, new Receiver(receiverMonitor));
            }
            SetGeneratorOnReceiver(receiver);
            return receiver;
        }

        /// <inheritdoc />
        public Task<object?> ExecuteCommand(int command, object? arg)
        {
            if (arg == null)
            {
                throw new ArgumentNullException(nameof(arg));
            }
            generatorConfig = arg as IStreamGeneratorConfig;
            if (generatorConfig == null)
            {
                throw new ArgumentOutOfRangeException(nameof(arg), "Arg must by of type IStreamGeneratorConfig");
            }

            // 更新接收器上的发电机
            foreach (var receiver in receivers)
            {
                SetGeneratorOnReceiver(receiver.Value);
            }

            return Task.FromResult<object?>(true);
        }

        private class Receiver : IQueueAdapterReceiver
        {
            private const int MaxDelayMs = 20;
            private readonly IQueueAdapterReceiverMonitor receiverMonitor;
            public IStreamGenerator? QueueGenerator { private get; set; }

            public Receiver(IQueueAdapterReceiverMonitor receiverMonitor)
            {
                this.receiverMonitor = receiverMonitor;
            }

            public Task Initialize(TimeSpan timeout)
                => Initialize(timeout, CancellationToken.None);

            public Task Initialize(TimeSpan timeout, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                this.receiverMonitor?.TrackInitialization(true, TimeSpan.MinValue, null);
                return Task.CompletedTask;
            }

            public async Task<IList<IBatchContainer>> GetQueueMessagesAsync(int maxCount)
                => await GetQueueMessagesAsync(maxCount, CancellationToken.None);

            public async Task<IList<IBatchContainer>> GetQueueMessagesAsync(int maxCount, CancellationToken cancellationToken)
            {
                var watch = Stopwatch.StartNew();
                await Task.Delay(Random.Shared.Next(1, MaxDelayMs), cancellationToken);
                List<IBatchContainer> batches;
                if (QueueGenerator == null || !QueueGenerator.TryReadEvents(DateTime.UtcNow, maxCount, out batches))
                {
                    return new List<IBatchContainer>();
                }
                watch.Stop();
                this.receiverMonitor?.TrackRead(true, watch.Elapsed, null);
                if (batches.Count > 0)
                {
                    var oldestMessage = batches[0] as GeneratedBatchContainer;
                    var newestMessage = batches[batches.Count - 1] as GeneratedBatchContainer;
                    this.receiverMonitor?.TrackMessagesReceived(batches.Count, oldestMessage?.EnqueueTimeUtc, newestMessage?.EnqueueTimeUtc);
                }
                return batches;
            }

            public Task MessagesDeliveredAsync(IList<IBatchContainer> messages)
                => MessagesDeliveredAsync(messages, CancellationToken.None);

            public Task MessagesDeliveredAsync(IList<IBatchContainer> messages, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }

            public Task Shutdown(TimeSpan timeout)
                => Shutdown(timeout, CancellationToken.None);

            public Task Shutdown(TimeSpan timeout, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                this.receiverMonitor?.TrackShutdown(true, TimeSpan.MinValue, null);
                return Task.CompletedTask;
            }
        }

        private void SetGeneratorOnReceiver(Receiver receiver)
        {
            // 如果我们没有发电机配置, 不要设置发电机
            var generatorConfig = this.generatorConfig;
            if (generatorConfig == null)
            {
                return;
            }

            var generator = (IStreamGenerator?)(serviceProvider.GetService(generatorConfig.StreamGeneratorType) ?? Activator.CreateInstance(generatorConfig.StreamGeneratorType));
            if (generator == null)
            {
                throw new OrleansException($"StreamGenerator type not supported: {generatorConfig.StreamGeneratorType}");
            }
            generator.Configure(serviceProvider, generatorConfig);
            receiver.QueueGenerator = generator;
        }

        /// <inheritdoc />
        public IQueueCache CreateQueueCache(QueueId queueId)
        {
            //将块状池创建从 Init 方法移到这里, 以避免在流提供商初始化时在客户端启动不必要的块状集合创建 。
            CreateBufferPoolIfNotCreatedYet();
            var dimensions = new CacheMonitorDimensions(queueId.ToString(), this.blockPoolMonitorDimensions.BlockPoolId);
            var cacheMonitorFactory = this.CacheMonitorFactory!; // Init () 期间设置 。
            var cacheMonitor = cacheMonitorFactory(dimensions);
            return new GeneratorPooledCache(
                bufferPool,
                this.loggerFactory.CreateLogger($"{typeof(GeneratorPooledCache).FullName}.{this.Name}.{queueId}"),
                serializer,
                cacheMonitor,
                this.statisticOptions.StatisticMonitorWriteInterval);
        }

        /// <summary>
        /// 创建新的 <see cref="GeneratorAdapterFactory"/> 实例。
        /// </summary>
        /// <param name="services">服务。</param>
        /// <param name="name">提供者名称。</param>
        /// <returns>新建的 <see cref="GeneratorAdapterFactory"/> 实例。</returns>
        public static GeneratorAdapterFactory Create(IServiceProvider services, string name)
        {
            var queueMapperOptions = services.GetOptionsByName<HashRingStreamQueueMapperOptions>(name);
            var statisticOptions = services.GetOptionsByName<StreamStatisticOptions>(name);
            var factory = ActivatorUtilities.CreateInstance<GeneratorAdapterFactory>(services, name, queueMapperOptions, statisticOptions);
            factory.Init();
            return factory;
        }

        // “找不到流提供者 {StreamProvider} 的发电机配置。 直到命令提供配置之前, 没有活动 。”
        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "No generator configuration found for stream provider {StreamProvider}.  Inactive until provided with configuration by command."
        )]
        private partial void LogInfoNoGeneratorConfigurationFound(string streamProvider);
    }
}
