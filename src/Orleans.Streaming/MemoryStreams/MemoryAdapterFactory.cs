using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO.Hashing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans.Configuration;
using Orleans.Providers.Streams.Common;
using Orleans.Runtime;
using Orleans.Streams;

namespace Orleans.Providers
{
    /// <summary>
    /// 内存流提供程序的适配器工厂。
    /// 此工厂同时充当适配器和适配器工厂。事件存储在内存中的 Grain 内，
    /// 该 Grain 充当事件队列。此提供程序适配器主要用于测试。
    /// </summary>
    public partial class MemoryAdapterFactory<TSerializer> : IQueueAdapterFactory, IQueueAdapter, IQueueAdapterCache
        where TSerializer : class, IMemoryMessageBodySerializer
    {
        private readonly StreamCacheEvictionOptions cacheOptions;
        private readonly MemoryStreamCacheOptions memoryCacheOptions;
        private readonly StreamStatisticOptions statisticOptions;
        private readonly HashRingStreamQueueMapperOptions queueMapperOptions;
        private readonly IGrainFactory grainFactory;
        private readonly ILoggerFactory loggerFactory;
        private readonly ILogger logger;
        private readonly TSerializer serializer;
        private readonly OrleansInstruments orleansInstruments;
        private readonly ulong _nameHash;
        private IStreamQueueMapper streamQueueMapper = null!; // 在使用之前由 Init() 设置。
        private ConcurrentDictionary<QueueId, IMemoryStreamQueueGrain> queueGrains = null!; // 在使用之前由 Init() 设置。
        private IObjectPool<FixedSizeBuffer>? bufferPool;
        private BlockPoolMonitorDimensions blockPoolMonitorDimensions = null!; // 在使用之前随 bufferPool 一同设置。
        private IStreamFailureHandler? streamFailureHandler;
        private TimePurgePredicate purgePredicate = null!; // 在使用之前由 Init() 设置。

        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        public bool IsRewindable => true;

        /// <inheritdoc />
        public StreamProviderDirection Direction => StreamProviderDirection.ReadWrite;

        /// <summary>
        /// 创建分区的故障处理程序。
        /// </summary>
        protected Func<string, Task<IStreamFailureHandler>>? StreamFailureHandlerFactory { get; set; }

        /// <summary>
        /// 创建缓存监视器以报告缓存相关指标。
        /// 返回 ICacheMonitor。
        /// </summary>
        protected Func<CacheMonitorDimensions, ICacheMonitor>? CacheMonitorFactory;

        /// <summary>
        /// 创建内存块池监视器以监控内存块池相关指标。
        /// 返回 IBlockPoolMonitor。
        /// </summary>
        protected Func<BlockPoolMonitorDimensions, IBlockPoolMonitor>? BlockPoolMonitorFactory;

        /// <summary>
        /// 创建监视器以监控 QueueAdapterReceiver 相关指标。
        /// 返回 IQueueAdapterReceiverMonitor。
        /// </summary>
        protected Func<ReceiverMonitorDimensions, IQueueAdapterReceiverMonitor>? ReceiverMonitorFactory;

        /// <summary>
        /// 初始化 <see cref="MemoryAdapterFactory{TSerializer}"/> 类的新实例。
        /// </summary>
        /// <param name="providerName">流提供程序名称。</param>
        /// <param name="cacheOptions">流缓存逐出选项。</param>
        /// <param name="statisticOptions">流统计选项。</param>
        /// <param name="queueMapperOptions">队列映射器选项。</param>
        /// <param name="serviceProvider">服务提供程序。</param>
        /// <param name="grainFactory">Grain 工厂。</param>
        /// <param name="loggerFactory">日志记录器工厂。</param>
        public MemoryAdapterFactory(
            string providerName,
            StreamCacheEvictionOptions cacheOptions,
            StreamStatisticOptions statisticOptions,
            HashRingStreamQueueMapperOptions queueMapperOptions,
            IServiceProvider serviceProvider,
            IGrainFactory grainFactory,
            ILoggerFactory loggerFactory)
        {
            this.Name = providerName;
            this.queueMapperOptions = queueMapperOptions ?? throw new ArgumentNullException(nameof(queueMapperOptions));
            this.cacheOptions = cacheOptions ?? throw new ArgumentNullException(nameof(cacheOptions));
            this.statisticOptions = statisticOptions ?? throw new ArgumentNullException(nameof(statisticOptions));
            this.grainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
            this.loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            this.logger = loggerFactory.CreateLogger<ILogger<MemoryAdapterFactory<TSerializer>>>();
            this.serializer = MemoryMessageBodySerializerFactory<TSerializer>.GetOrCreateSerializer(serviceProvider);
            this.orleansInstruments = serviceProvider.GetRequiredService<OrleansInstruments>();
            this.memoryCacheOptions = serviceProvider.GetOptionsByName<MemoryStreamCacheOptions>(providerName);

            var nameBytes = BitConverter.IsLittleEndian ? MemoryMarshal.AsBytes(Name.AsSpan()) : Encoding.Unicode.GetBytes(Name);
            XxHash64.Hash(nameBytes, MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref _nameHash, 1)));
        }

        /// <summary>
        /// 初始化此实例。
        /// </summary>
        [MemberNotNull(nameof(CacheMonitorFactory), nameof(BlockPoolMonitorFactory), nameof(ReceiverMonitorFactory))]
        public void Init()
        {
            this.queueGrains = new ConcurrentDictionary<QueueId, IMemoryStreamQueueGrain>();
            if (CacheMonitorFactory == null)
                this.CacheMonitorFactory = (dimensions) => new DefaultCacheMonitor(dimensions, this.orleansInstruments);
            if (this.BlockPoolMonitorFactory == null)
                this.BlockPoolMonitorFactory = (dimensions) => new DefaultBlockPoolMonitor(dimensions, this.orleansInstruments);
            if (this.ReceiverMonitorFactory == null)
                this.ReceiverMonitorFactory = (dimensions) => new DefaultQueueAdapterReceiverMonitor(dimensions, this.orleansInstruments);
            this.purgePredicate = new TimePurgePredicate(this.cacheOptions.DataMinTimeInCache, this.cacheOptions.DataMaxAgeInCache);
            this.streamQueueMapper = new HashRingBasedStreamQueueMapper(this.queueMapperOptions, this.Name);
        }

        [MemberNotNull(nameof(bufferPool))]
        private void CreateBufferPoolIfNotCreatedYet()
        {
            if (this.bufferPool == null)
            {
                // 使用 1 MB 内存块的池。
                this.blockPoolMonitorDimensions = new BlockPoolMonitorDimensions($"BlockPool-{Guid.NewGuid()}");
                var oneMb = 1 << 20;
                var blockPoolMonitorFactory = this.BlockPoolMonitorFactory!; // 在 Init() 中设置。
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
            return streamQueueMapper;
        }

        /// <inheritdoc />
        public IQueueAdapterReceiver CreateReceiver(QueueId queueId)
        {
            var dimensions = new ReceiverMonitorDimensions(queueId.ToString());
            var receiverLogger = this.loggerFactory.CreateLogger($"{typeof(MemoryAdapterReceiver<TSerializer>).FullName}.{this.Name}.{queueId}");
            var receiverMonitorFactory = this.ReceiverMonitorFactory!; // 在 Init() 中设置。
            var receiverMonitor = receiverMonitorFactory(dimensions);
            IQueueAdapterReceiver receiver = new MemoryAdapterReceiver<TSerializer>(GetQueueGrain(queueId), receiverLogger, this.serializer, receiverMonitor);
            return receiver;
        }

        /// <inheritdoc />
        public async Task QueueMessageBatchAsync<T>(StreamId streamId, IEnumerable<T> events, StreamSequenceToken? token, Dictionary<string, object>? requestContext)
        {
            try
            {
                var queueId = streamQueueMapper.GetQueueForStream(streamId);
                ArraySegment<byte> bodyBytes = serializer.Serialize(new MemoryMessageBody(events.Cast<object>(), requestContext));
                var messageData = MemoryMessageData.Create(streamId, bodyBytes);
                IMemoryStreamQueueGrain queueGrain = GetQueueGrain(queueId);
                await queueGrain.Enqueue(messageData, CancellationToken.None);
            }
            catch (Exception exc)
            {
                LogErrorQueueMessageBatchAsync(exc);
                throw;
            }
        }

        /// <inheritdoc />
        public IQueueCache CreateQueueCache(QueueId queueId)
        {
            // 将内存块池的创建从初始化方法移到这里，避免在客户端初始化流提供程序时创建不必要的内存块池。
            CreateBufferPoolIfNotCreatedYet();
            var logger = this.loggerFactory.CreateLogger($"{typeof(MemoryPooledCache<TSerializer>).FullName}.{this.Name}.{queueId}");
            var cacheMonitorFactory = this.CacheMonitorFactory!; // 在 Init() 中设置。
            var monitor = cacheMonitorFactory(new CacheMonitorDimensions(queueId.ToString(), this.blockPoolMonitorDimensions.BlockPoolId));
            return new MemoryPooledCache<TSerializer>(bufferPool, purgePredicate, logger, this.serializer, monitor, this.statisticOptions.StatisticMonitorWriteInterval, this.cacheOptions.MetadataMinTimeInCache, this.memoryCacheOptions.MaxAddCount);
        }

        /// <inheritdoc />
        public Task<IStreamFailureHandler> GetDeliveryFailureHandler(QueueId queueId)
        {
            return Task.FromResult(streamFailureHandler ?? (streamFailureHandler = new NoOpStreamDeliveryFailureHandler()));
        }

        /// <summary>
        /// 根据队列 ID 生成确定性的 Guid。
        /// </summary>
        private Guid GenerateDeterministicGuid(QueueId queueId)
        {
            Span<byte> bytes = stackalloc byte[16];
            MemoryMarshal.Write(bytes, in _nameHash);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[8..], queueId.GetUniformHashCode());
            BinaryPrimitives.WriteUInt32LittleEndian(bytes[12..], queueId.GetNumericId());
            return new(bytes);
        }

        /// <summary>
        /// 根据队列 ID 获取 MemoryStreamQueueGrain 实例。
        /// </summary>
        private IMemoryStreamQueueGrain GetQueueGrain(QueueId queueId)
        {
            return queueGrains.GetOrAdd(queueId, (id, arg) => arg.grainFactory.GetGrain<IMemoryStreamQueueGrain>(arg.instance.GenerateDeterministicGuid(id)), (instance: this, grainFactory));
        }

        /// <summary>
        /// 创建新的 <see cref="MemoryAdapterFactory{TSerializer}"/> 实例。
        /// </summary>
        /// <param name="services">服务提供程序。</param>
        /// <param name="name">提供程序名称。</param>
        /// <returns>新的 <see cref="MemoryAdapterFactory{TSerializer}"/> 实例。</returns>
        public static MemoryAdapterFactory<TSerializer> Create(IServiceProvider services, string name)
        {
            var cachePurgeOptions = services.GetOptionsByName<StreamCacheEvictionOptions>(name);
            var statisticOptions = services.GetOptionsByName<StreamStatisticOptions>(name);
            var queueMapperOptions = services.GetOptionsByName<HashRingStreamQueueMapperOptions>(name);
            var factory = ActivatorUtilities.CreateInstance<MemoryAdapterFactory<TSerializer>>(services, name, cachePurgeOptions, statisticOptions, queueMapperOptions);
            factory.Init();
            return factory;
        }

        [LoggerMessage(
            EventId = (int)ProviderErrorCode.MemoryStreamProviderBase_QueueMessageBatchAsync,
            Level = LogLevel.Error,
            Message = "Exception thrown in MemoryAdapterFactory.QueueMessageBatchAsync."
        )]
        private partial void LogErrorQueueMessageBatchAsync(Exception exception);
    }
}
